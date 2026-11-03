# Programa de Cálculo Estrutural Brasileiro — escopo organizado

Nome definido pelo usuário em 03/11/2026: **Programa de Cálculo Estrutural Brasileiro** — sigla **PCEB**.

Reorganização de `requisitos_originais.md` e dos pedidos posteriores. O que é dúvida está em
**Perguntas em aberto**, no fim.

## 1. Visão
Um programa **grande**, do escritório, de cálculo estrutural **brasileiro**: normas ABNT e conceitos consagrados no meio
nacional (ABECE, Gerdau, manuais e livros clássicos — ver `02_biblioteca/fontes_referencia.md`).
Começa por **pré-moldados** e cobre
**análise → dimensionamento → detalhamento → fabricação → documentação**, com:
- interface **web** (primeiro local, depois na nuvem);
- programa **Windows** com as mesmas funções;
- **plugins** para Revit e CAD (DXF) para automação e documentação;
- **biblioteca** de normas, livros e padrões do escritório como base técnica.

### Estratégia (definida pelo usuário em 03/11/2026)
1. **Agora**: extrair as funcionalidades de todos os programas catalogados e trazê-las para dentro do **Revit**,
   que é o **modelador 3D principal**.
2. **Depois**: o programa próprio PCEB (web + Windows, **um núcleo só**), para não depender do Revit —
   mas sem abandoná-lo: o PCEB terá interface própria **e poderá operar o Revit** (comandar o plugin a partir do programa).
3. **Programa livre**, mantido com doações.

Consequência para a arquitetura: o núcleo (cálculo, normas, regras de detalhamento, catálogos) deve nascer **fora**
do Revit desde já, para servir ao plugin hoje e ao PCEB amanhã.

## 2. Componentes
| # | Componente | Papel | Origem no texto |
|---|---|---|---|
| C1 | Interface web | estudo, análise e demais funções | Contém 1; Req. 1 |
| C2 | Programa Windows | mesmas funções da web | Req. 2 |
| C3 | Plugin Revit | automação, documentação, possivelmente modelo 3D — **hoje: StructuralDetailer** | Contém 2 e 3; Req. 6 |
| C4 | Plugin CAD / DXF | automação e documentação 2D (GstarCAD, AutoCAD via DXF) — **hoje: Armadura BR 0.1.23** | Contém 2 |
| C5 | Biblioteca técnica | normas, livros, artigos, padrões | Ref. 3 |
| C6 | Conteúdo parametrizado | famílias RVT, seções, modelos de vista | Req. 7 |

## 3. O que a solução resolve
1. Análise estrutural
2. Dimensionamento estrutural
3. Detalhamento estrutural
4. Projetos de fabricação
5. Emissão de arquivos e documentação — `ifc, dxf, xls, csv, xml, json, cam, step`…
6. Importação/exportação para outras ferramentas — ex.: SAP2000 `$2k / s2k` (texto)

### 3.1 Tipos de análise — o que já está no estudo
O texto original diz só "análise estrutural". Detalhando (situação em 03/11/2026):

| Tipo | Relevância para pré-moldados | Situação no estudo |
|---|---|---|
| Linear estática | base de tudo | nas fichas |
| **Não linear geométrica** (2ª ordem, P-Δ) | **alta**: pilar em balanço engastado no cálice | nas fichas (SAP, Robot, OpenSees, TQS) |
| **Não linear física** (fissuração, EI reduzido NBR 6118, fibras) | alta | nas fichas (OpenSees, TQS, concreteproperties) |
| **Ligações semirrígidas** (NBR 9062, fator de restrição αR) | **alta**: típico de pré-moldado | **não estava** — incluir |
| **Fases construtivas** (articulado → engastado, montagem) | alta | nas fichas (SAP, Robot, TQS, IDEA) |
| **Situações transitórias** (desforma, içamento, transporte) | alta | só no rascunho de notas — incluir |
| Contato / regiões locais (consolos, dentes, apoios) | alta | nas fichas (CalculiX, PrePoMax, IDEA) |
| **Dinâmica**: modal, espectral (sismo NBR 15421), vibração, vento dinâmico (NBR 6123) | média | **não estava** como requisito — incluir |
| **Vento estático** (NBR 6123, coeficientes) | alta (galpões) | **não estava** — incluir |
| **Vento por CFD** (simulação de escoamento) | baixa/pontual: formas atípicas | **não estava** — ver nota |
| Fadiga (pontes rolantes em galpões) | média | **não estava** |
| Incêndio (NBR 15200) | média | **não estava** |
| Protensão (perdas, fases) | alta para vigas | parcial (TQS, IDEA Beam) |

Nota sobre **CFD**: na prática de projeto o vento entra pela NBR 6123 (coeficientes de forma e pressão).
CFD (open source: **OpenFOAM**, Code_Saturne; comerciais/nuvem: Ansys, SimScale) serve para formas fora da norma e
exige muito processamento. Sugestão: catalogar o OpenFOAM, mas deixar CFD como fase futura.

Desempenho, paralelismo e interfaces gráficas: ver `01_catalogo/interfaces_e_desempenho.md`.

## 4. Organização do desenvolvimento
- Ambiente de desenvolvimento e ambiente de produção separados.
- Plugin Revit modular, com boas práticas de organização de arquivos.

## 5. Escopo de estruturas
O escritório não trabalha só com pré-moldados (ampliação de 03/11/2026):

| Sistema | Normas principais (conferir edições na biblioteca) | Situação |
|---|---|---|
| Concreto pré-moldado | NBR 9062, NBR 6118 | **início** |
| Concreto moldado in loco | NBR 6118, NBR 6122 (fundações) | escopo |
| Aço — perfis laminados e soldados | NBR 8800 | escopo |
| Aço — perfis formados a frio | NBR 14762 (e NBR 6355 para os perfis) | escopo |
| Steel frame (light steel framing) | NBR 16970, NBR 14762 | futuro |
| Wood frame | NBR 7190 | futuro |
| Ações comuns a todos | NBR 6120 (cargas), NBR 6123 (vento), NBR 8681 (combinações), NBR 15421 (sismo), NBR 15200 / 14323 (incêndio) | escopo |

**Ordem definida (03/11/2026)**:
1. **Pré-moldados** — vigas, pilares, painéis, consolos, toda a parte de pré-moldado; começa **genérico** e
   é lapidado conforme evolui.
2. **Fundações**.
3. **Estruturas metálicas** (NBR 8800 e 14762).
4. Depois: concreto in loco, steel frame, wood frame (ordem a definir).

## 5.1 Robustez (requisito não funcional)
O programa e os plugins não podem fechar sozinhos nem perder trabalho quando um comando falha.
Estudo em `robustez_e_estabilidade.md`.

## 6. Plano de trabalho (esqueleto — detalhar depois)
| Fase | Conteúdo | Situação |
|---|---|---|
| 0 | Estudo: catálogo de programas, biblioteca, casos-teste | **em andamento** |
| 1 | Arquitetura: núcleo único fora do Revit; unificar regras com o Armadura BR | a fazer |
| 2 | Pré-moldados genéricos no Revit (vigas, pilares, painéis, consolos): **análise linear estática** → dimensionamento → detalhamento → prancha | parcial (pilares no StructuralDetailer) |
| 3 | Fundações | a fazer |
| 4 | Estruturas metálicas | a fazer |
| 5 | Programa PCEB (web local + Windows) operando o Revit; depois nuvem, CAM, demais sistemas e análises | a fazer |

## Decisões do usuário (03/11/2026)
| # | Assunto | Decisão |
|---|---|---|
| D1 | Por onde começar | **Toda a parte de pré-moldado** (vigas, pilares, painéis, consolos), genérico e lapidando |
| D2 | Modelador 3D | **Revit** é o modelador principal |
| D3 | Web × Windows | **um núcleo só** |
| D4 | CAM | fica para depois |
| D5 | Distribuição | **programa livre**, com doações |
| D6 | Biblioteca | usuário insere depois, ou pede para eu organizar |
| D7 | Regras de detalhamento | **unificar** pelo que está no Armadura BR (GstarCAD) — ele é a referência |
| D8 | Análise | começar pela **análise linear estática** |
| D9 | Ordem dos sistemas | pré-moldados → **fundações** → **estruturas metálicas** |
| D10 | Nome | **PCEB**; pasta `PCEB` |
| D11 | Licença e versionamento | **GPL-3** (`LICENSE`); versionado em **Git**; o usuário controla o que entra e sai |
| D12 | Normas | **não publicadas**; ficam no computador do usuário. A pasta `02_biblioteca/normas` existe no repositório com `NORMAS_REFERENCIA.txt` (nomes usados) para cada usuário alimentar a sua |

## Perguntas em aberto
13. **GPL-3 vale também para o Armadura BR e o StructuralDetailer?** Hoje o Armadura BR é privado e tem versão
    demo de 1 hora — num programa livre a demo deixaria de fazer sentido.
14. **Exceção de ligação para Revit/CAD**: plugins GPL que usam APIs fechadas (Revit, AutoCAD, GstarCAD) ficam numa
    zona cinzenta da GPL. O usual é acrescentar uma "permissão adicional" (seção 7 da GPL-3) autorizando a ligação
    com essas APIs. Incluir?
15. **Livros, artigos e padrões do escritório**: deixei fora do Git como as normas (direitos autorais e projetos de
    clientes). Algum padrão do escritório pode ser público (ex.: templates sem dados de cliente)?
