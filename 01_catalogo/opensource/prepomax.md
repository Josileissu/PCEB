# PrePoMax

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| Matej Borovinšek, Univ. de Maribor (gitlab.com/MatejB/PrePoMax) | **GPL-3** | **C# / .NET**, Windows | não |

## Funções
- Interface gráfica de **pré e pós-processamento para o CalculiX** (que vem junto).
- **Geometria**: importa STEP, IGES, STL, BREP e malhas `.inp`/`.unv`; operações simples de geometria.
- **Malha**: Netgen e Gmsh (versões recentes), refinamento local, malhas de sólidos e cascas.
- **Modelo**: materiais, seções, restrições, contato, cargas, condições de contorno, passos de análise —
  em árvore com histórico (regenera ao trocar a geometria).
- **Análises**: as do CalculiX (estática, frequências, flambagem, térmica, contato, não linear).
- **Resultados**: mapas de cor, deformada, animação, gráficos, cortes, exportação de resultados.
- **Dimensionamento / detalhamento**: não.

## Avaliação (provisória)
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 4 | uso crescente em ensino e indústria |
| Manutenção | 4 | autor ativo, versões frequentes |
| Documentação | 3 | tutoriais e vídeos; código é a referência |
| Integração | 4 | **mesma linguagem do nosso plugin (C#)** — dá para estudar como monta o `.inp` e lê o `.frd` |
| Aderência | 3 | regiões locais de pré-moldados (consolos, dentes), não pórtico |
| Licença | 3 | GPL-3: estudar e usar à parte, sem copiar código para o nosso programa fechado |

## Uso possível para nós
- Caminho mais rápido para **ver** os consolos do PP127 em MEF (exportar a geometria STEP do Revit → PrePoMax).
- Estudar a arquitetura de um programa de MEF em .NET (modelo, malha, solver externo, pós).

## A confirmar
- Versão atual; se a exportação de STEP do Revit chega limpa.
