# OpenSees / OpenSeesPy

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| UC Berkeley / PEER (opensees.berkeley.edu, github.com/OpenSees) | **acadêmica**: livre para ensino/pesquisa/sem fins lucrativos; uso comercial requer contato | C++; uso por Tcl ou Python (`openseespy`) | não |

## Funções
- **Modelagem**: por script (nós, elementos, materiais, seções); sem interface gráfica própria
  (interfaces de terceiros: STKO (comercial), OpenSees Navigator, GiD).
- **Análise**: linear e **não linear** (geométrica e material), P-Δ / corrotacional, estática incremental (pushover),
  modal, dinâmica no tempo, **estágios** (ativar/desativar elementos), interação solo-estrutura.
- **Elementos**: barras elásticas, barras não lineares por **fibras** (força/deslocamento), cascas, sólidos, molas/zero-length.
- **Materiais**: muitos modelos de concreto (Concrete01/02/04/…) e aço (Steel01/02…), uniaxiais e nD.
- **Dimensionamento / detalhamento**: não — é motor de cálculo.
- **Saídas**: recorders (texto), Python: acesso direto a deslocamentos e esforços.

## Avaliação (provisória)
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 5 | 25+ anos, referência em pesquisa sísmica |
| Manutenção | 4 | ativo no GitHub |
| Documentação | 3 | wiki e manuais extensos mas dispersos |
| Integração | 4 | Python direto; resultado em memória |
| Aderência | 3 | ótimo para pilar em balanço com 2ª ordem física/geométrica; nada de NBR |
| Licença | 2 | **restrição comercial** — verificar antes de usar no escritório |

## Uso possível para nós
- Validar 2ª ordem de pilares pré-moldados (não linearidade física e geométrica real × métodos aproximados da NBR).
- Estudo de fases de montagem (articulado → engastado).

## A confirmar
- Texto atual da licença; wheel do `openseespy` para Python 3.14 (provavelmente precisa de venv com versão anterior).
