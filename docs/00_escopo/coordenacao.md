# Quadro de coordenação — PC1 × PC2

Cada Claude lê este quadro no **início** da sessão e atualiza no **fim** (situação, branch, próximo passo).
Regras em `docs/04_arquitetura/trabalho_em_paralelo.md`.

Situação: a fazer · em andamento · aguardando usuário · pronto p/ merge · feito

## PC2 — estudo (programas informados pelo usuário: AutoCAD, Revit, CYPE, Eberick, SAP2000, IDEA StatiCa, Tekla, Tekla Structures)
| # | Tarefa | Situação | Branch | Entrega / observação |
|---|---|---|---|---|
| P2-1 | Preparar o PC2 (Git, gh, .NET 8, Claude Code, clone) | a fazer | — | passos em `trabalho_em_paralelo.md` |
| P2-2 | **Inventário do PC2**: todos os programas estruturais/CAD instalados, versões, caminhos, API/SDK (o usuário citou "Tekla" e "Tekla Structures": conferir o que é cada um). Rodar `docs/notas/ferramentas/raiox.py` em Tekla, IDEA e AutoCAD e mandar o resultado em `notas/pc2_pendencias.md` (o PC1 junta na tabela 1.1 de `interfaces_e_desempenho.md`) | a fazer | `pc2/inventario` | `docs/01_catalogo/inventario_pc2.md`, no formato do `inventario_local.md` |
| P2-3 | Ficha do **Tekla** conferida no programa (✓): detalhamento de pré-moldados, pranchas, numeração, quadros, Open API | a fazer | `pc2/estudo-tekla` | atualizar `01_catalogo/comerciais/tekla.md` |
| P2-4 | Ficha do **IDEA StatiCa** conferida (✓): módulo Detail para consolos e dentes Gerber, normas (tem NBR?), IOM | a fazer | `pc2/estudo-idea` | atualizar `01_catalogo/comerciais/idea-statica.md` |
| P2-5 | **Matriz de detalhamento**: como Tekla, CYPE, Eberick e IDEA tratam os 15 elementos da D19 | a fazer | `pc2/matriz-detalhamento` | `01_catalogo/matriz_detalhamento.md` (o PC1 preenche TQS e DEB) |
| P2-6 | Testar o **DEB.Revit** no Revit do PC2 (versão?) com o AutoRun | a fazer | — | depende dos arquivos privados no Drive |
| P2-7 | **Template de reservatório** com reconhecimento (pedido do usuário no PC2, 05/11) | pausado (decisão do usuário) | `pc2/template-reservatorio` | v1: `TemplateReservatorio` + `ReconhecedorReservatorio` (Core) + ARMRESERVATORIO; testado no AutoCAD 2026 com o DWG de teste (Eberick). Na `main` em 05/11. Ver `src/DEB.Cad/docs/TEMPLATES.md`. **Ordem decidida (05/11):** 1) template de LAJES, 2) template de PAREDES (como pilar e base de reservatório), com os esquemas de armação de cada um; 3) detalhe dos CANTOS e encontros; 4) juntar tudo no template de reservatório |
| P2-8 | **Template de viga pré-moldada** (pedido do usuário no PC2, 06/11): levantar os requisitos, depois as informações do detalhamento; o usuário vai enviar o padrão | em andamento (aguardando o padrão) | `pc2/viga-premoldada` | `src/DEB.Cad/docs/VIGA_PREMOLDADA_REQUISITOS.md` (normas conferidas, categorias VP-1…VP-5, entrada, perguntas da seção 14). Padrão em `src/DEB.Cad/info_auxiliar/templates/viga_pre_moldada/` |
| P2-9 | **Template de blocos sobre estacas** (pedido do usuário no PC2, 06/11: B3 + exemplos B4/B15/B28/B31/B3.1) | pronto p/ revisão do usuário | `pc2/template-bloco-estacas` | padrão BPM; grade e triangular; cálice com colarinho/embutido/arranque; `BLOCOS_REQUISITOS.md` + `TEMPLATES.md`; 209 testes ok; ARMTESTEBLOCO 8 ok (AutoCAD 2026). Detalhes padrão do pilar expostos (`DesenharDetalhesComuns`) |
| P2-10 | **Motor de detalhamento: contratos + bloco como piloto** (pedido do usuário no PC2, 07/11: "organizar e trabalhar em várias frentes") | em andamento | `pc2/motor-detalhamento` (+ frentes A–D) | contratos em `DEB.Core/Motor/Contratos.cs` e `04_arquitetura/motor_contratos.md`. **PC1: o P1-2 (motor de lineares) deve usar os mesmos contratos**. 07/11: bloco montado pela receita JSON; limpeza (peso de aço = regras da RA via `MedidaBarra`; `DetalhesTipicos`/`IdiomaDesenho` fora do pilar; `Geometria2D`; `GeradorEstribo.FechadoPadrao`; `LeitorJson`). ARMTEMPLATE do bloco passou ao motor (`TemplateBlocoMotor`: mesma janela/modelos; origem — elemento, perfil, sobreposições, sobrescritas — no XData "MOT" do detalhamento); ARMTESTEBLOCO 10 ok no AutoCAD 2026. Template antigo ainda no código até o usuário conferir no CAD |

## PC1 — código
| # | Tarefa | Situação | Branch | Entrega / observação |
|---|---|---|---|---|
| P1-1 | Pilar pré-moldado no Revit igual à PP127 | aguardando usuário | — | o usuário vai testar a janela e mandar prints e a lista de diferenças |
| P1-2 | Levar a lógica do pilar para o `DEB.Core` (motor de lineares + perfil JSON) | a fazer | `pc1/motor-lineares` | depois do P1-1 |
| P1-3 | Matriz de detalhamento (TQS, Eberick, CYPE, Revit, DEB) | feito 05/11 | `pc1/matriz-detalhamento` | `01_catalogo/matriz_detalhamento.md`; faltam Tekla e IDEA (P2-5) |
| P1-4 | Raio-X dos programas do PC1 (interface, gráfico, geometria, cálculo, automação) | feito 05/11 | `pc1/catalogo-programas` | tabela 1.1 em `interfaces_e_desempenho.md` |
| P1-5 | Fichas completas do PC1: TQS, Eberick, CYPE 3D, SAP2000, Robot, Revit, GstarCAD | feito 05/11 | `pc1/catalogo-programas` | o PC2 só completa o que depende dos programas de lá (Tekla, IDEA, AutoCAD, CYPECAD se houver) |

## Aguardando o usuário
- Link de origem do **GN Corbel** (o instalador baixado só tem o ID da Store do ChatGPT).
- Pasta privada no Google Drive para Projeto1.rvt, PP127 e padrões do escritório (para o PC2).
