# Quadro de coordenação — PC1 × PC2

Cada Claude lê este quadro no **início** da sessão e atualiza no **fim** (situação, branch, próximo passo).
Regras em `docs/04_arquitetura/trabalho_em_paralelo.md`.

Situação: a fazer · em andamento · aguardando usuário · pronto p/ merge · feito

## PC2 — estudo (programas informados pelo usuário: AutoCAD, Revit, CYPE, Eberick, SAP2000, IDEA StatiCa, Tekla, Tekla Structures)
| # | Tarefa | Situação | Branch | Entrega / observação |
|---|---|---|---|---|
| P2-1 | Preparar o PC2 (Git, gh, .NET 8, Claude Code, clone) | em andamento | — | 05/11: Git, .NET 8, Claude Code, clone e `S:\Meu Drive\Normas` ok; **falta o `gh`** (instalar e `gh auth login`) |
| P2-2 | **Inventário do PC2**: todos os programas estruturais/CAD instalados, versões, caminhos, API/SDK (o usuário citou "Tekla" e "Tekla Structures": conferir o que é cada um) | pronto p/ merge | `pc2/inventario` | `docs/01_catalogo/inventario_pc2.md`. Tekla = Tekla Structures 2025 (detalhamento, ambiente Brazil) + Structural Designer 2025 + Tedds |
| P2-3 | Ficha do **Tekla** conferida no programa (✓): detalhamento de pré-moldados, pranchas, numeração, quadros, Open API | em andamento | `pc2/estudo-tekla` | 05/11: `comerciais/tekla.md` conferida pelos arquivos do ambiente Brazil (aços e dobras × NBR 6118, componentes, pranchas, relatórios, Open API). Falta abrir o programa: licença, prancha de pilar × PP127 |
| P2-4 | Ficha do **IDEA StatiCa** conferida (✓): módulo Detail para consolos e dentes Gerber, normas (tem NBR?), IOM | a fazer | `pc2/estudo-idea` | atualizar `01_catalogo/comerciais/idea-statica.md` |
| P2-5 | **Matriz de detalhamento**: como Tekla, CYPE, Eberick e IDEA tratam os 15 elementos da D19 | a fazer | `pc2/matriz-detalhamento` | `01_catalogo/matriz_detalhamento.md` (o PC1 preenche TQS e DEB) |
| P2-6 | Testar o **DEB.Revit** no Revit do PC2 (versão?) com o AutoRun | em andamento | — | 05/11: AutoRun `precast` no Revit 2025 do PC2 com `Teste_PM_entrada.rvt` (4 P1 40×40 + 2 P2 50×50): **OK**, 2 pranchas, grupos e giros certos. Achados p/ o PC1: (1) seções 1-1/2-2 e cortes A-A/B-B caem sobre as tabelas e o carimbo; (2) aço CA25 nas barras de 8/12,5/16 (esperado CA-50?); (3) Revit × núcleo: estribo +6,8 cm, grampo +2,0, costura do consolo +9,0, tirante −0,9; (4) P2: ρ = 0,39% < 0,40%; (5) o modelo de teste não tem as legendas nem o parâmetro "Título" no carimbo. Revit no PC2 abre devagar (~9 min) no ambiente do Claude: rede bloqueada na licença |
| P2-0 | DEB.Cad no AutoCAD 2026 do PC2 (pedido do usuário antes da divisão) | feito | `main` (f4dfa56) | palette com 2 barras, texto da cópia mantém rotação (XData `DROT`), MATCHPROP do plugin (barra/faixa/tabela), faixas ortogonal/alinhada sem diâmetro. Ver `src/DEB.Cad/CLAUDE.md`. GstarCAD/ZWCAD sem teste (não instalados aqui) |

## PC1 — código
| # | Tarefa | Situação | Branch | Entrega / observação |
|---|---|---|---|---|
| P1-1 | Pilar pré-moldado no Revit igual à PP127 | aguardando usuário | — | o usuário vai testar a janela e mandar prints e a lista de diferenças |
| P1-2 | Levar a lógica do pilar para o `DEB.Core` (motor de lineares + perfil JSON) | a fazer | `pc1/motor-lineares` | depois do P1-1 |
| P1-3 | Colunas TQS e DEB da matriz de detalhamento | a fazer | `pc1/matriz-detalhamento` | junta com o P2-5 |

## Aguardando o usuário
- Link de origem do **GN Corbel** (o instalador baixado só tem o ID da Store do ChatGPT).
- Pasta privada no Google Drive para Projeto1.rvt, PP127 e padrões do escritório (para o PC2).
