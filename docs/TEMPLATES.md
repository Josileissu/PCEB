# Templates de armadura (ambiente preparado em 2026-10-27)

Pedido do usuário: templates para os elementos com padrão estabelecido no escritório. A implementação
de cada um começa quando ele enviar os PDFs/DWGs (pastas em `info_auxiliar/templates/<elemento>/`).

## Elementos previstos (`CategoriaElemento`)
| Categoria | Variações citadas |
|---|---|
| Base circular de reservatório | fundação |
| Base retangular de reservatório | fundação |
| Bloco de 1 estaca | com/sem colarinho ou cálice; embutimento para pilar pré-moldado |
| Bloco de 2 estacas | idem |
| Pilar pré-moldado | levantamento em `docs/PILAR_PREMOLDADO_REQUISITOS.md` (2026-10-29): categorias PP-1…PP-5, normas, padrão P61/PP127 |
| Viga pré-moldada | protendida ou não |
| Pilar moldado in loco | |
| Viga moldada in loco | |
| Laje | |

## Estrutura do código
- `src/Armadura.Core/Templates/Templates.cs` (testável, sem CAD):
  - `ITemplateArmadura`: `Id`, `Nome`, `Categoria`, `Descricao`, `Parametros`, `Gerar(valores, cfg)`;
  - `ParametroTemplate` (número, inteiro, texto, opção, bitola, sim/não; grupo, unidade, limites);
  - `ValoresTemplate` (valores digitados, com leitura numérica tolerante a vírgula);
  - `ResultadoTemplate`: `Barras` (`BarraTemplate`: vértices em cm relativos à inserção, bulges,
    formatos dos segmentos/ganchos, sobrepostos, cotados, bitola, aço, quantidade, espaçamento, estribo),
    `Contornos` (linhas de forma) e `Avisos`;
  - `CatalogoTemplates`: registro, busca por categoria, `Validar`.
- `src/Armadura.Cad/ComandosTemplate.cs`: comando **ARMTEMPLATE** (janela com a árvore de elementos e os
  parâmetros num PropertyGrid dinâmico → ponto de inserção → desenho), `Desenhar` (contornos + armaduras
  pelo `ServicoBarra.Criar`). Sem templates registrados, o comando lista os elementos previstos.
- Estribos fechados em qualquer poligonal: `GeradorEstribo.FechadoPoligono` (o template monta a volta e
  escolhe o canto dos ganchos).

## Para implementar um template
1. Nova classe em `src/Armadura.Core/Templates/<Elemento>/`, implementando `ITemplateArmadura`.
2. Registrar no carregamento do plugin (`CatalogoTemplates.Registrar`).
3. Testes de unidade com as medidas de um projeto real (do PDF/DWG do usuário) e uma verificação no autoteste.
4. Próximos passos previstos: faixas geradas pelo template (distribuição), cortes, quadro de ferros por
   elemento, salvar conjuntos de valores ("predefinições") por elemento.

## Implementados
### Base de reservatório (`Templates/BaseReservatorio/TemplateBaseReservatorio.cs`, 2026-10-27)
Padrão dos projetos Assaí SPA/SRP/ASC (2024–2025). Circular ou retangular, num template só.
- Parâmetros: geometria (forma, D ou Lx/Ly, h, cobrimentos 4/5, reservatório Ø e capacidade), estacas (anéis,
  grade ou nenhuma), malha inferior e superior (aço, bitola, espaçamento, dobra: até a outra face/fixa/sem),
  faixa central reforçada (largura e bitola), borda (U: bitola, espaçamento, aba; retangular: barras de borda
  por face; circular: anel com nº de barras, bitola e emenda), malha intermediária (barras ou tela, camadas),
  barras VAR (posição única ou uma por barra), escala e nome do detalhamento.
- Distribuição SIMÉTRICA (sobra metade de cada lado); quantidade pela mesma regra das faixas.
- Circular: cada família vira um grupo VAR (`DadosArmadura.ComprimentosVar`, XData "CVAR") com tabela de
  comprimentos "L"; ou, na opção "uma posição por barra", um N para cada comprimento (simétricas juntas).
- Saída: planta de locação e formas, planta de armaduras com faixas, coluna "detalhes das barras", corte 1-1
  com indicações, tabelas "L", notas, e tudo num detalhamento (bloco) com a RA parcial ("nome no título").
- Fora do escopo (pedido do usuário): nichos de concretagem, chumbadores, tabela de cargas das estacas.
[VALIDAR com o usuário no uso: posição das faixas, tamanhos de texto, valores padrão das dobras.]

### Pilar pré-moldado PP-1 (`Templates/PilarPreMoldado/TemplatePilarPreMoldado.cs`) — em construção
Requisitos e regras de norma: `docs/PILAR_PREMOLDADO_REQUISITOS.md`. Verdadeira grandeza (cm); a escala de cada
região só dá a altura dos textos/cotas (a folha é montada com viewports, como no DWG do P61). Seção b (x) × h (y);
face 1 = +x, 2 = −y, 3 = −x, 4 = +y; a VISTA n olha a face n (1 e 3 mostram h, 2 e 4 mostram b).
- **Etapa 1 (2026-10-29, feita)**: 4 vistas lado a lado (passo = maior lado + 20 alturas de texto), níveis (topo e base
  sempre + lista "NOME/NAME=cota"), cadeia de cotas entre níveis e "COMPRIMENTO TOTAL DO PILAR: <>", embutimento com
  chaves (linhas a cada passo/dente nas 4 vistas, cota na VISTA 2, avisos da tab. 15 e do mínimo de 40 cm), chanfros,
  marcas do corte A-A, corte A-A com as vistas em volta, notas (cobrimento por face, tolerâncias da tab. 2, insertos) e
  informações p/ produção (volume da forma, peso a 2500 kgf/m³). Idioma PT, EN ou PT/EN.
  Quadro de produção EM TABELA (modelo do usuário, 2026-10-29): título "PT\P(EN)", rótulo à esquerda e valor com
  unidade à direita ("fcj = 28 MPa", "1,209 m³", "3,023 ton.", "1 un."); linha da montagem só com fcj > 0.
  `TabelaTemplate` ganhou larguras/alinhamentos por coluna e quebra de linha na célula (`TabelaTemplate.Quebra`).
  GstarCAD 2021: `HorizontalCellMargin`/`VerticalCellMargin` não têm efeito e a altura mínima da linha vem do estilo;
  o espaço inicial afasta o texto da borda esquerda, o final é descartado [pendente: estilo de tabela próprio].
- **Seções e cortes (plano, 2026-10-29)**: incrementais — gerados onde há o que mostrar: corte de forma no topo
  (insertos/gabarito) e um corte por nível de consolo (B-B, C-C…, hachurado); seções de armadura onde o arranjo das
  longitudinais muda (topo com grampos U, trecho corrente, cada redução/emenda, base) e no consolo; marcas nas vistas
  e na elevação das armaduras; seções iguais não se repetem.
- **Etapa 2 (2026-10-29, feita)** — `ArmadurasPilar.cs` (partial): longitudinais por parâmetros (canto com 1 barra ou 3
  em "L" a 5 cm, intermediárias por face b/h; centro do canto = cobrimento da face + φt + φ/2, o P61 dá 5,55), emendas
  ALTERNADAS (metade das barras de cada grupo começa com peça menor: emendas afastadas ≥ 1,3 l0) ou NA MESMA SEÇÃO, ou
  sem emendas (rolo; aviso se a bitola só existe em barras); traspasse = NBR 6118 com o fck do pilar (50 % ou 100 %
  emendadas) e nunca < 45φ, arredondado a 5 cm, com cota e NOTA PT/EN ao lado ("AJUSTAR SE NECESSÁRIO"); estribos
  fechados (ganchos de 90° no canto superior esquerdo, medidas externas = seção − cobrimentos) por zona (base, corrente,
  topo; um N por bitola), faixas na elevação sem o diâmetro ("71 N1 c/15"); grampos suplementares (uma ou duas
  direções; quadrado = um N com "2x"); grampos U no topo e na base (duas direções; a 2ª recuada pelo canto em "L");
  seções 1-1/2-2/3-3 com as barras em ponto (diâmetro real), estribo, grampos, pernas dos U e UMA INDICAÇÃO POR BARRA
  (textos em ordem dos dois lados, sem cruzar); formas dos estribos/grampos abaixo das seções; RA do detalhamento à
  direita. Avisos da NBR 6118 (18.4.2.1, 18.4.2.2, 18.4.3, 18.2.4). Descrição das barras: `BarraTemplate.PosicaoDescricao`
  e `RotacaoDescricao` (na vertical, ao lado da barra, como no P61).
  Correção no plugin: a INDICAÇÃO não passa mais o espaçamento dela (15) para a barra (`ServicoFaixa.Criar`).
- **Ajustes de 2026-10-29**: quadro de produção com `Table.SetMargin` por célula (a margem global não funciona no
  GstarCAD) e `SetTextHeight` (sem ele o texto ficava com a altura do estilo); colunas 24/11 alturas de texto. Seções:
  cada longitudinal com a chamada na altura da própria barra (afastada só o necessário: passadas de cima para baixo e
  de volta, depois recentradas); U com UMA chamada por conjunto (direção x ao lado, direção y em cima).
- **Etapa 3 (2026-10-29, feita)** — `ConsolosPilar.cs`: 2 consolos parametrizados (faces "1,3" = duplo com tirante
  passante; face sozinha = tirante com gancho de 90° no pilar), cota do topo, b, a, h e h1 (trapezoidal), deslocamento;
  vistas: de frente (retângulo + aresta da ponta + pinos + almofada) e de lado (trapézio + pino + almofada + cotas na
  VISTA 2); cortes de forma B-B/C-C hachurados (ANSI31; `HachuraTemplate`) com almofada, pinos e cotas, e marcas na
  VISTA 2; detalhe "ARMADURAS CONSOLO b x h x a - DUPLO" abaixo das vistas (corte com as barras e indicações + formas:
  tirante, barra transversal soldada ou laço horizontal, costura (estribo horizontal em volta), estribos verticais,
  inferior em L, pinos em L; almofada "NEOPRENE a x b x e mm"); a2 da NBR 9062 7.3.3.4 posiciona a almofada e os pinos
  (no centro dela); volume com os consolos (P61 = 3,523 m³ / 8,808 t, igual à prancha) e `AlturaCG` (para as alças).
  Avisos 7.3.2.2, 7.3.3.1, 7.3.3.4, 7.3.3.6/7, 7.3.3.12, 7.3.6.
- **Catálogo de consolos padrão (2026-10-29, pedido do usuário)**: `TipoConsolo` (nome, descrição, duplo, b, a, h, h1,
  armaduras padronizadas) na configuração do usuário (`ConfiguracaoArmadura.Consolos`, DataMember 68; padrão = P61 duplo,
  PP127 simples e trapezoidal); comando **ARMCONSOLOS** (`Paleta/FormConsolos.cs`: lista + PropertyGrid, novo/duplicar/
  excluir, nomes únicos); `TemplatePilarPreMoldado.Catalogo` (estático, o Plugin põe o da configuração ao carregar e
  depois do ARMCONSOLOS) dá as opções. No pilar: **5 posições** (C1…C5) com tipo, faces, **distância da base ao topo do
  consolo** (cm) e deslocamento. Tipo duplo exige faces opostas; tipo simples em "1,3" = dois consolos ancorados.
  Um detalhe de armaduras por tipo (e largura do pilar na direção do balanço), com as quantidades somadas.
  `Parametros` virou propriedade calculada (as opções mudam com o catálogo). Testes do pilar na coleção "Pilar" (sem
  paralelismo: o catálogo é estático).
- **Etapa 4 (2026-10-29, feita)** — `ItensPilar.cs`: alças de cordoalha CP-190 (NBR 9062 5.3.3.5) simétricas ao CG
  (`PosicaoAlcas`: CG ± (50 − pct) % de L; pct 20; valores digitados mandam; aviso perto de consolo na mesma face), na
  face escolhida (2): de lado o laço fora da face, de frente o traço com cotas 25|25 e "ALÇA", cotas base→alça→alça→topo;
  detalhe "ALÇA DE IÇAMENTO" 1:20 com a barra (pé + pernas + laço; "2x2"). Montagem: furo ø100 a pct do topo (30 %,
  aviso fora de 25–35 %) nas faces 1-3 ou 2-4 (círculo de frente, linhas ocultas de lado, cota ao topo) com o detalhe
  "REFORÇO NOS FUROS" (grampos U, r = ø/2 + 3 → C=125 do P61, 8 por furo), ou alça de topo (+1 alça). Inserto: nome
  acima da VISTA 1, chumbadores nas vistas, gabarito com cotas em cadeia no corte A-A. Detalhes padrão: chaves (1:10),
  espaçamento no canto (1:5, com os valores reais), tabela de raio de curvatura (NBR 6118), emendas por traspasse e por
  solda, comprimento das barras (A1/B1), ATENÇÃO SPDA (PT/EN). O x do próximo detalhe respeita a largura do título.
- **Ajustes de 2026-10-29 (3ª rodada)**:
  - **Parâmetro tabela** (`TipoParametro.Tabela`, `ColunaTabela`; valor = células "|", linhas ";"): no ARMTEMPLATE vira
    "N consolos" com botão "…" (`Paleta/FormTabela.cs`: grade, opções em lista, adicionar/remover/subir/descer, **Ctrl+V
    do Excel**). Usado em **Consolos** (tipo, faces, base→topo, deslocamento — sem limite de quantidade), **Níveis**
    (PT, EN, cota; `LerNiveis` aceita também o formato antigo) e **Insertos** (tipo, local Topo/Face n, base→centro,
    deslocamento).
  - Grupos com dois dígitos ("01." … "16."): o PropertyGrid ordena categorias como texto.
  - **Catálogo de insertos** (`TipoInserto`, config DataMember 69; ARMINSERTOS): modo topo (chumbadores + gabarito, corte
    A-A com cotas em cadeia nas duas direções) ou chapa na face (chapa de frente; espessura e grapas de lado). Não entram na RA.
  - Janela de catálogo genérica `FormCatalogo<T>` (`Paleta/FormConsolos.cs`) para consolos e insertos.
  - **Chaves de cisalhamento** refeitas como o detalhe 01 do P61 (`PerfilChaves`): rebaixo de "dente" recuado "prof",
    trecho de passo − dente até a face com chanfros, arestas atravessando o pilar dentro de um círculo, cadeias 4/1 e 5,
    chamadas CHAVES/PILAR. Nas vistas, linhas nas arestas do rebaixo.
  - **Detalhe típico** como a prancha do escritório (`DetalheTipico`): títulos sublinhados com SEM ESCALA e NBR-6118,
    ganchos desenhados + tabela, emendas por traspasse (barras hachuradas, ≥ 45ø e o par com ≥ 9ø), por solda (cordões
    ≥ 5ø e corte a-a ≥ 0,3ø), comprimento das barras (U com r, A1/B1) e notas PT/EN em colunas.
- Próximas: entrada da armadura longitudinal por tabela/Excel (posições, bitolas, redução com a altura — PP-2) (entram no volume e
  no CG); 4 alças pelo CG, furo/alça de topo, inserto, detalhes padrão; 5 entrada por tabela/Excel/janela.
