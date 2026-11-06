# Quadro de coordenação — PC1 × PC2

Cada Claude lê este quadro no **início** da sessão e atualiza no **fim** (situação, branch, próximo passo).
Regras em `docs/04_arquitetura/trabalho_em_paralelo.md`.

Situação: a fazer · em andamento · aguardando usuário · pronto p/ merge · feito

## PC2 — estudo (programas informados pelo usuário: AutoCAD, Revit, CYPE, Eberick, SAP2000, IDEA StatiCa, Tekla, Tekla Structures)
| # | Tarefa | Situação | Branch | Entrega / observação |
|---|---|---|---|---|
| P2-1 | Preparar o PC2 (Git, gh, .NET 8, Claude Code, clone) | em andamento | — | 05/11: Git, .NET 8, Claude Code, clone e `S:\Meu Drive\Normas` ok; **falta o `gh`** (instalar e `gh auth login`) |
| P2-2 | **Inventário do PC2**: todos os programas estruturais/CAD instalados, versões, caminhos, API/SDK (o usuário citou "Tekla" e "Tekla Structures": conferir o que é cada um). Rodar `docs/notas/ferramentas/raiox.py` em Tekla, IDEA e AutoCAD e mandar o resultado em `notas/pc2_pendencias.md` (o PC1 junta na tabela 1.1 de `interfaces_e_desempenho.md`) | inventário pronto p/ merge; **falta o raio-X** | `pc2/inventario` | `docs/01_catalogo/inventario_pc2.md`. Tekla = Tekla Structures 2025 (detalhamento, ambiente Brazil) + Structural Designer 2025 + Tedds |
| P2-3 | Ficha do **Tekla** conferida no programa (✓): detalhamento de pré-moldados, pranchas, numeração, quadros, Open API | em andamento | `pc2/estudo-tekla` | 05/11: `comerciais/tekla.md` conferida pelos arquivos do ambiente Brazil (aços e dobras × NBR 6118, componentes, pranchas, relatórios, Open API). Falta abrir o programa: licença, prancha de pilar × PP127 |
| P2-4 | Ficha do **IDEA StatiCa** conferida (✓): módulo Detail para consolos e dentes Gerber, normas (tem NBR?), IOM | a fazer | `pc2/estudo-idea` | atualizar `01_catalogo/comerciais/idea-statica.md` |
| P2-5 | **Matriz de detalhamento**: como Tekla, CYPE, Eberick e IDEA tratam os 15 elementos da D19 | a fazer | `pc2/matriz-detalhamento` | `01_catalogo/matriz_detalhamento.md` (o PC1 preenche TQS e DEB) |
| P2-6 | Testar o **DEB.Revit** no Revit do PC2 (versão?) com o AutoRun | em andamento | — | 05/11: AutoRun `precast` no Revit 2025 do PC2 com `Teste_PM_entrada.rvt` (4 P1 40×40 + 2 P2 50×50): **OK**, 2 pranchas, grupos e giros certos. Achados p/ o PC1: (1) seções 1-1/2-2 e cortes A-A/B-B caem sobre as tabelas e o carimbo; (2) aço CA25 nas barras de 8/12,5/16 (esperado CA-50?); (3) Revit × núcleo: estribo +6,8 cm, grampo +2,0, costura do consolo +9,0, tirante −0,9; (4) P2: ρ = 0,39% < 0,40%; (5) o modelo de teste não tem as legendas nem o parâmetro "Título" no carimbo. Revit no PC2 abre devagar (~9 min) no ambiente do Claude: rede bloqueada na licença |
| P2-0 | DEB.Cad no AutoCAD 2026 do PC2 (pedido do usuário antes da divisão) | feito | `main` (f4dfa56) | palette com 2 barras, texto da cópia mantém rotação (XData `DROT`), MATCHPROP do plugin (barra/faixa/tabela), faixas ortogonal/alinhada sem diâmetro. Ver `src/DEB.Cad/CLAUDE.md`. GstarCAD/ZWCAD sem teste (não instalados aqui) |

## PC1 — código
| # | Tarefa | Situação | Branch | Entrega / observação |
|---|---|---|---|---|
| P1-1 | Pilar pré-moldado no Revit igual à PP127 | **em andamento no PC2** (ordem do usuário, 05/11) | `pc2/pilar-premoldado` | feitos (05/11): CA-50, carimbo A1, padrão `PCEB-PPM-A1` (v21) no repo, templates `PCEB-PPM-*`, embutimento, níveis em todas as vistas, títulos acima das vistas, elevação da armadura esquemática, chamadas N nas seções, formas em cm, quadro do consolo (planta/corte 1:10 + formas) com as barras novas (barra soldada, estribos verticais, inferior em U, pinos); (06/11) chamadas N no quadro do consolo (corte: todas as barras + "Solda"; planta: pinos) e título "ARMADURAS CONSOLE … - n UN.", CORTE A-A (transversal) e B-B; cotas do quadro só do consolo; formas dos estribos e grampos embaixo da última seção (coluna das seções com a altura toda da folha); cota acumulada nas vistas de face (uma cadeia base → consolos → topo, total por fora); marcas de corte simples (A/B nas faces, 1/2 na armadura). Comparação com o pilar padrão do CAD em `docs/04_arquitetura/padroes_apresentacao.md`. Próximos: detalhes padrão (dependem das legendas do escritório), grampos U na elevação esquemática. Testes no Revit 2026 do PC2 (o 2025 é o do usuário) |
| P1-2 | Levar a lógica do pilar para o `DEB.Core` (motor de lineares + perfil JSON) | a fazer | `pc1/motor-lineares` | depois do P1-1 |
| P1-3 | Matriz de detalhamento (TQS, Eberick, CYPE, Revit, DEB) | feito 05/11 | `pc1/matriz-detalhamento` | `01_catalogo/matriz_detalhamento.md`; faltam Tekla e IDEA (P2-5) |
| P1-4 | Raio-X dos programas do PC1 (interface, gráfico, geometria, cálculo, automação) | feito 05/11 | `pc1/catalogo-programas` | tabela 1.1 em `interfaces_e_desempenho.md` |
| P1-5 | Fichas completas do PC1: TQS, Eberick, CYPE 3D, SAP2000, Robot, Revit, GstarCAD | feito 05/11 | `pc1/catalogo-programas` | o PC2 só completa o que depende dos programas de lá (Tekla, IDEA, AutoCAD, CYPECAD se houver) |

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
