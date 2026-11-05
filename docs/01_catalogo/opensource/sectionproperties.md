# sectionproperties (+ concreteproperties)

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| Robbie van Leeuwen (github.com/robbievanleeuwen) | **MIT** | Python | não |

## sectionproperties
- **Geometria**: seções quaisquer (polígonos, furos, compostas), biblioteca de perfis, importa DXF e shapely.
- **Análise por MEF 2D**: área, inércias, módulos resistentes, centro de gravidade, eixos principais,
  **torção (J)**, **empenamento**, centro de cisalhamento, módulos plásticos.
- **Tensões** na seção para N, Mx, My, Vx, Vy, Mz; seções com **vários materiais**.
- Gráficos (matplotlib).

## concreteproperties (mesmo autor, MIT)
- Seções de **concreto armado/protendido**: propriedades brutas e transformadas, **momento resistente**,
  **diagrama de interação N-M**, flexão oblíqua (biaxial), momento-curvatura, **fissuração** e tensões em serviço.
- Leis de material configuráveis (parábola-retângulo, aço elastoplástico) — permite aplicar os
  parâmetros da **NBR 6118**, ainda que a norma não venha pronta.

## Avaliação (provisória)
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 4 | muito usado; versões estáveis |
| Manutenção | 4 | ativo |
| Documentação | 5 | documentação e exemplos excelentes |
| Integração | 5 | Python puro; ótimo para o servidor da interface web |
| Aderência | 5 | seções de vigas/pilares pré-moldados (I, T, retangulares com recortes), flexão composta de pilares |
| Licença | 5 | MIT — pode embutir |

## Uso possível para nós
- Propriedades de seções de vigas pré-moldadas (I, T invertido, protendidas) e **flexão composta oblíqua de pilares**.
- Base do módulo de **dimensionamento** (o que hoje falta no StructuralDetailer).

## A confirmar
- Compatibilidade com Python 3.14; conferir resultados contra exemplos da NBR 6118 / livros da biblioteca.

## Raio-X do código-fonte (05/11/2026) ✓
Lido direto do pacote em `programas/*/download` com `docs/notas/ferramentas/raiox_fonte.py` (sem compilar nem rodar).
- Pequeno: 63 arquivos Python, ~24 mil linhas; numpy, scipy, **shapely**, numba (opcional). MIT ✓.
- Módulos: `geometry`, `pre`/`fea`/`solver`/`post` (MEF de seção com triângulos de 6 nós), `stress_post`,
  bibliotecas `concrete_sections`, `steel_sections`, `timber_sections`, `bridge_sections`, `rhino` (importa do Rhino).
- O **concreteproperties** (flexão composta, diagramas M-N) é outro pacote e **não está no download**.
