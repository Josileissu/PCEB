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

### Plano de ataque (05/11/2026)
Com a D19 (detalhamento primeiro), o estudo do catálogo passa a olhar primeiro o **detalhamento**; análise e
dimensionamento ficam registrados, mas sem aprofundar agora.

| Etapa | O que | Entrega |
|---|---|---|
| A | Fechar pendências curtas do catálogo: GN Corbel (aguarda link), linha "materiais/sistemas cobertos" nas fichas. Aço (mCalc, Dimperfil), vento (Visual Ventos) e steel/wood frame **adiados** para a fase de análise | fichas atualizadas |
| B | **Obra-piloto**: escolher uma obra real do escritório com o máximo de elementos da lista D19; reunir as pranchas dela como gabarito | `03_casos-teste/obra-piloto/` (fora do Git se for de cliente) |
| C | **Matriz elemento × programa** só de detalhamento: para cada um dos 15 elementos, como TQS, Eberick, CYPE, Tekla, Revit e DEB (CAD/Revit) detalham — entradas, regras, saídas (vistas, cortes, quadro de aço), o que é configurável | `01_catalogo/matriz_detalhamento.md` |
| D | **Regras por elemento**: NBR 6118 (cap. 18 e 20–22), NBR 9062, manuais (ABECE, Gerdau) e o padrão do escritório, conferidas com o DEB.Cad (D7) | `04_arquitetura/regras_detalhamento/<elemento>.md` |
| E | Motor (`motor_detalhamento.md`): núcleo comum → pilar pré-moldado → demais, na ordem do plano | código + testes |

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

| D13 | Plugins | **GPL-3 também para o Armadura BR e o StructuralDetailer** |
| D14 | APIs fechadas | **exceção de ligação** (GPL-3 seção 7) em `LICENSE-EXCEPTION.md`: Revit, AutoCAD, Robot, GstarCAD, ZWCAD, CSI OAPI, TQS |
| D15 | Livros, artigos, padrões | **fora do Git**; padrões do escritório **não** são públicos |

| D16 | Repositório | **um único repositório (PCEB)** com núcleo, plugins e o futuro programa |
| D17 | Nome dos plugins | **Detalhador Estrutural Brasileiro**, no CAD (ex-Armadura BR) e no Revit (ex-StructuralDetailer) |
| D18 | Ports futuros | Blender, FreeCAD e o próprio PCEB |
| D19 | Prioridade (05/11/2026) | **motor de detalhamento antes das análises estruturais**: detalhar uma obra inteira primeiro — rotinas paramétricas + templates de dados, 5 motores; plano em `04_arquitetura/motor_detalhamento.md` |
| D20 | Catálogo (05/11/2026) | aço (mCalc, Dimperfil), vento (Visual Ventos) e steel/wood frame **adiados** para a fase de análise; Tekla será estudado depois, no outro computador do usuário (onde está instalado) |
| D21 | Ordem (05/11/2026) | **obra-piloto ainda não** (etapa B adiada); primeiro fazer o **detalhamento no Revit** (etapa E) |
| D22 | Revit → CAD (09/11/2026) | as peças do modelo Revit vão para o CAD como **dados**, não como desenho: o DEB.Revit exporta os grupos de peças iguais (iguais ou giradas) num JSON (`DEB.Core/Intercambio`) e o DEB.Cad gera um detalhamento por grupo pelo template, com as repetições = nº de peças. 1º passo: pilares pré-moldados (botão "Exportar para o CAD" + ARMIMPORTARPECAS); 2º: vigas pré-moldadas (dentes Gerber pela geometria, invertidas = iguais) |
| D23 | Vigas pré-moldadas (11/11/2026) | **2º estágio**: porta-estribo em cima, por dentro do estribo; a **armadura superior é suprimida** (vai no detalhe do capeamento da laje; o cálculo futuro das vigas fará as adequações). **Construtiva do ressalto** = as 4 longitudinais dele. **Furos de pino no dente**: largura < 30 cm → 1 furo; ≥ 30 cm → 2. **Consolo inclinado na lateral da viga**: adiado ("não vamos continuar com os consolos por hora") |
| D24 | Lajes alveolares (12/11/2026) | comando **"Paginar laje alveolar"** no DEB.Revit (motor em `DEB.Core/Lajes`, migrado de um protótipo testado num modelo real). Regras: módulo 125 (painel 124,5 + junta 0,5); ajuste mínimo 30 cm (sobra menor → dois ajustes; sem como → faixa maciça, etapa 2); comprimento = face da alma − 1,5 cm em cada ponta, apoio mínimo 8 cm; viga sem aba = mesma folga + alerta; aberturas: junta no meio da abertura com dois painéis recortados; pilares → recortes; faixa maciça ou peça cortada por furo junto a viga → alerta "apoio em consolo a colocar"; etapas 1 painéis, 2 chaves/faixas maciças, 3 capa; suportes de instalações liga/desliga (tirante Ø12,5, chapas 300×300×6, malha 1,25 × 1,25). Estratégias: aberturas primeiro (padrão), melhor distribuição, de um lado. Família do painel escolhida pelo usuário; catálogos de fornecedor fora do Git. **Barra do tirante que não cabe na parte estreita da junta: furo em obra na borda dos dois painéis** (Ø = barra + 3 mm, configurável; sem recortar a geometria; anotado no tirante e listado no relatório por painel) |
| D25 | Pranchas de formas (13/11/2026) | reproduzir no Revit a apresentação das pranchas de formas do escritório (locação com tabela de cargas, montagem por pavimento com tabela de lajes, cortes) e depois **automatizar no DEB.Revit**. Textos por **identificador/parâmetro**; tabelas por **parâmetros dos elementos** (criar os que faltarem, prefixo `SD_`). **Elementos especiais** (caixa de escada, caixa de elevador em alvenaria estrutural) com prancha própria; alvenaria **sem armadura** por enquanto. Plano em `04_arquitetura/pranchas_de_formas.md` |
| D26 | Núcleo único e prévia (15/11/2026) | Toda regra de engenharia, número e texto de prancha mora no **`DEB.Core`**. O DEB.Cad e o DEB.Revit só fazem três coisas: **ler** do programa hospedeiro, **chamar** o núcleo e **escrever** o resultado. Toda operação que cria ou altera geometria segue **modelo no núcleo → prévia → aceite do usuário → aplicação**, e a prévia e o resultado saem do **mesmo modelo** (nunca de dois cálculos); referência: paginação de laje alveolar (D24). Ficam fora da prévia as operações de um clique sem geometria nova. Fica no adaptador o que é próprio do hospedeiro (XData, famílias, Extensible Storage, a janela da prévia). Primeiro caso: pilar pré-moldado no Revit pelas regras do núcleo, com teste de paridade antes. |
| D27 | Organização do núcleo (15/11/2026) | A pasta é o **elemento** que o engenheiro nomeia (viga, pilar, laje, consolo, fundação, reservatório…); subtipos ficam dentro (pilar pré-moldado, metálico, parede). A **dimensão** (barra, superfície, volume) é declarada no contrato do elemento e escolhe o algoritmo; não vira pasta. **Composição**: consolo, conexão e treliça são conjuntos que referenciam outros elementos; o elemento não conhece quem o usa. Pasta nova só nasce junto com código (sem pastas vazias). A reorganização é feita por partes: primeiro `Templates/`, o resto quando for tocado. "Perfil" passa a significar perfil metálico; o padrão de detalhamento do escritório passa a se chamar **padrão**. |
| D28 | Seções, produtos e sistemas (15/11/2026) | Três níveis, cada um com o seu mecanismo. **(1) Seção de elemento linear = família + parâmetros + partes nomeadas.** Concreto: retangular, T, L, I, trapezoidal; "invertido" é orientação da mesma família. Aço: I/H (W, H, HP, I), U, Ue, Z, tubo (circular, quadrado, retangular), trilho; as dimensões vêm de **catálogo em dados** (uma tabela por fabricante ou norma). A família gera o contorno para desenhar e conferir; a seção poligonal medida fica só para o que não casa com nenhuma família. Consolo, dente, enrijecedor de alma e abertura são **acréscimos ao longo do eixo**, presos a uma parte nomeada da seção. **(2) Produto de superfície = peça de catálogo + paginação sobre um pano**: lajes (alveolar, steel deck, vigota com enchimento, nervurada), painéis e telhas; o motor de paginação da D24 é generalizado para "peça de largura útil sobre pano com recortes". **(3) Sistema = receita que gera elementos** a partir de poucos parâmetros (treliça, joist, conexão). Cadastro de catálogo é dado e pode crescer; família ou motor novo só entra quando uma entrega pedir. Catálogos em PDF e marcas ficam fora do Git. |
| D29 | Montagem de folhas no Revit (15/11/2026) | Para folha nova, o programa (e a IA que o desenvolve) entrega **só as vistas prontas**; o usuário organiza a folha à mão; depois o arranjo dele é **lido** (posição, escala e ordem das vistas) e vira o padrão a reproduzir. Não se gasta sessão tentando acertar o arranjo por tentativa. Começa no próximo projeto em que for preciso. |
| D30 | Regras do pilar pré-moldado: Revit × CAD (16/11/2026, com revisão no mesmo dia) | Divergências da paridade (`04_arquitetura/paridade_pilar.md`), uma regra do núcleo para os dois: **(1)** ganchos de **135°** no estribo, na costura e no estribo vertical do consolo. **(2)** **Sem estribo repetido** na divisa entre zonas (a divisa pertence à zona seguinte) — vale também na **viga pré-moldada**; estribos começam **no cobrimento das pontas**, como no CAD. **(3)** Tirante e costura do consolo sozinho param **por dentro das longitudinais** da face oposta; consolos em faces opostas **em nível** "se tornam um tipo, com tirante igual" (duplo, tirante e costura passantes; tolerância 0,6 cm); em níveis diferentes, um por consolo. Largura da costura **por dentro dos estribos verticais** (a do CAD). **(4)** Tabelas e peso do Revit pelo **comprimento do núcleo** (parâmetros `SD_` gravados na barra). **(5)** Alças em **cordoalha** (como o CAD), **simétricas ao CG ao longo do eixo e perto de 20 % das extremidades** ("simétrico ao eixo de gravidade, porém perto de 20% das extremidades. No eixo longitudinal"), pernas até **por dentro das longitudinais** da face oposta ("para por dentro das longitudinais"), topo do laço em **semicírculo** também no Revit (comprimento igual), **2 cordoalhas por ponto** e **face escolhida pelo usuário** (padrão sul, como no CAD — leitura do coordenador do "Ok" do dono). **(6)** Estribo vertical e armadura inferior do consolo: **definidos pelo usuário** ("é inserido pelo usuário"), não unificados. **(7)** Zonas de estribos que se sobrepõem: vale o **menor espaçamento**, nos dois programas (uma função do núcleo). |

## Perguntas em aberto
16. **Titular do copyright**: "Projeto PCEB" (10/11/2026: o usuário pediu para tirar o nome do escritório do repositório público).
17. **Versão demo de 1 hora do Armadura BR**: num programa livre deixa de fazer sentido. Remover?
18. ~~StructuralDetailer no Git~~ — respondida por D16 (entra no repositório do PCEB).
19. ~~Etapa 0~~ — **feita pelo usuário** (commits c599805 e 667a25a no armadura, já no GitHub).
20. ~~GitHub~~ — **feito (03/11/2026)**: `armadura-gstarcad` renomeado para **github.com/Josileissu/PCEB** (privado); remoto local do armadura atualizado.
21. ~~Sigla do plugin~~ — **respondida**: **DEB** (Detalhador Estrutural Brasileiro) nos nomes técnicos (pastas, DLLs, namespaces).
