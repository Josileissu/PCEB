# CalculiX

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| G. Dhondt e K. Wittig (calculix.de) | **GPL-2** | C / Fortran; solver `ccx`, pré/pós `cgx` | não |

## Funções
- **Entrada**: arquivo `.inp` no estilo Abaqus (texto) — fácil de gerar por programa.
- **Análise**: estática linear e não linear (geométrica, plasticidade, **contato**), frequências, flambagem,
  dinâmica, térmica e termomecânica acoplada.
- **Elementos**: sólidos (tetra/hexa, lineares e quadráticos), cascas, vigas (expandidas para sólidos internamente), molas.
- **Materiais**: elástico, plasticidade metálica, hiperelástico, materiais do usuário (UMAT).
  Concreto fissurado: sem modelo nativo conhecido (confirmar).
- **Saídas**: `.frd` (resultados), `.dat` (tabelas).
- **Dimensionamento / detalhamento**: não.

## Avaliação (provisória)
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 4 | décadas de uso industrial |
| Manutenção | 4 | versões regulares |
| Documentação | 3 | manual completo, estilo técnico |
| Integração | 4 | executável + arquivo texto; pode rodar em servidor (web) |
| Aderência | 3 | regiões locais: consolos, dentes, insertos, chapas de apoio (tensões elásticas) |
| Licença | 4 | GPL: usar como **programa externo** é tranquilo |

## Uso possível para nós
- Tensões locais em consolos/dentes/insertos para confrontar com biela-tirante.
- Motor de MEF por trás da interface web (via PrePoMax ou geração própria de `.inp`).

## A confirmar
- Modelos de material para concreto; desempenho em Windows (binários e paralelismo).

## Raio-X do código-fonte (05/11/2026) ✓
Lido direto do pacote em `programas/*/download` com `docs/notas/ferramentas/raiox_fonte.py` (sem compilar nem rodar).
- **O download só tem o `cgx` 2.23** (pré/pós gráfico), **não o solver `ccx`** — baixar o `ccx` se formos testar.
- `cgx`: C, ~190 mil linhas; interface **GLUT + OpenGL** (glut 3.5 incluído); malhas com **TetGen** e Netgen;
  NURBS com libSNL; 40 MB de manual e 23 MB de exemplos. GPL-2 ✓.
