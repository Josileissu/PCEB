# Análise por objetos e janela do modelo de análise (D44, D46)

O modelo de análise é do PCEB (decisão de 24/11/2026: fora do Revit). O Revit é só lido; nada é escrito no modelo.
Botão **Pré-moldados › Modelo de análise** (`Commands/ModeloAnaliticoCommand.cs`).

## Do Revit para os objetos (`Analise/EstruturaRevit.cs`)
| No Revit | Objeto (núcleo, `DEB.Core/Analise/Objetos`) | No modelo de barras |
|---|---|---|
| Pilar estrutural (levantamento dos pré-moldados) | `PilarObjeto` | barras no eixo (CG), nós nos níveis dos consolos/vigas/lajes, engaste no topo do cálice (embutimento) |
| Viga com consolo aninhado apoiado no pilar | `VigaObjeto` + `LigacaoConsolo` | barra no CG; vínculo rígido do eixo do pilar ao topo do consolo; mola do aparelho (Kh = G·A/t, Kv = 5·G·S²·A/t) |
| Outra viga (linha de locação reta) | `VigaObjeto` + `LigacaoPilarViga` | vínculo rígido ao pilar onde a ponta cai: monolítica (moldada no local) ou articulada (pré-moldada) |
| Piso estrutural | `LajeObjeto` | grelha (analogia de Hambly: I = b·h³/12, J = b·h³/6), cargas por m² nos nós, nós ligados às vigas e pilares embaixo; malha passando pelos eixos das vigas, pilares e aberturas |
| Piso alveolar / treliçado / vigota protendida (nome do tipo ou `PCEB_Tipo de laje`) | `LajeObjeto` unidirecional | faixas no vão (`SpanDirectionAngle`), transversal com 0,1 %, cada vão biapoiado; peso pela fração maciça da configuração ou `PCEB_Peso próprio (kN/m²)`; **sem verificação**: Md e Vd por metro para o catálogo |

- Seções: da geometria levantada (`PropriedadesSecao`: área, inércias, produto de inércia, torção por Prandtl), não das
  propriedades analíticas do Revit.
- Cargas: vigas pelos parâmetros `PCEB_Carga…` (D40); lajes por `PCEB_Carga permanente (kN/m²)` e `PCEB_Carga variável (kN/m²)`
  (por nome) ou pela configuração (`acoes.lajeG`, `acoes.lajeQ`); peso próprio pelo material.
- Sem aparelho de apoio na família: elastômero padrão da configuração (10 cm × largura da viga − 4 × 1 cm), com aviso.
- Viga apoiada em viga: ligação articulada no ponto de apoio (a viga de suporte ganha um nó ali).
- Laje apoiada direto no pilar: o nó da grelha sobre o pilar é preso a ele — o momento negativo ali é de pico (singularidade da
  grelha); para armar, use o valor na face do pilar.
- Aberturas de piso (laços internos da face de cima) saem da grelha: sem nós, barras nem carga.
- Ainda não: vento, combinações completas
  (há um conjunto mínimo provisório — o usuário deixou as combinações para depois).

## Solver (`DEB.Core/Analise/Barras`)
Pórtico espacial linear: rótulas por condensação, molas, vínculos rígidos mestre-escravo, apoios com mola, perfil (skyline) com
Cuthill-McKee reverso. Conferido com fórmulas fechadas, com o OpenSees e com o sectionproperties (scripts em
`tests/DEB.Core.Tests/Gabaritos`). Deformada ao longo da barra exata (Hermite + solução particular).

## Dimensionamento (`DEB.Core/Analise/Dimensionamento`)
Envoltória ELU por barra e uma verificação por tipo de objeto: viga (flexão, bielas, estribos, flecha estimada), pilar (etapa 3:
flexo-compressão oblíqua com 2ª ordem local — `Elementos/Pilar/Calculo/FlexoCompressaoObliqua.cs`, parábola-retângulo, domínios,
curva de interação, rigidez κ; conferida com o concreteproperties; códigos PA1–PA4), consolo (cálculo do núcleo com a armadura do
detalhamento), laje (armadura por metro, flecha; pré-fabricada só os esforços para o catálogo).
O pilar usa a armadura do detalhamento (longitudinais na seção logo acima do engaste) ou propõe a menor que passa. Na janela e no
relatório dos **pilares pré-moldados** a mesma verificação roda com a armadura escolhida ali (`ColumnRebarSpec.UsarEsforcosPilar`). Cada peça sai com situação, resumo, verificações e memória.

## Janela (`DEB.Core/Analise/Objetos/TelaAnalise.html` + `VisualizacaoAnalise`; WebView2 do Revit)
3D (girar, mover, zoom, vistas prontas) com modos Modelo, Deformada, Diagrama (momentos/cortantes/normal/torção do lado
tracionado), Mapa de cores e Situação; **2D da peça** (M, V, N do resultado + envoltória ELU tracejada); **Laje · isovalores**
(mx, my, mxy, flecha). Lista de objetos com a situação; painel com esforços nas pontas, forças no aparelho, reações e o
dimensionamento (verificações e memória).

## Testar
AutoRun `analitico` (sem gravar): grava `janela_<vista>.png` de seis vistas e o resumo do dimensionamento com a memória.
`SD_LAJE=1` cria um piso de 12 cm sobre as vigas do modelo aberto, para exercitar as lajes. Prévia fora do Revit: o teste
`EstruturaObjetosTests` com `PCEB_PREVIA=<pasta>` grava `previa_analise.html` e `previa_laje.html` (abrir no navegador).

## Estruturas de aço (D48)
- Vigas e pilares com material estrutural "Aço" viram `BarraMetalicaObjeto` (`src/Analise/MetalicaRevit.cs`): perfil pelo nome do
  tipo (`W310X38.7`, `W 310 x 38,7`, `CVS 400x200x9.5x6.3`) ou pelas dimensões da seção da família; aço pelo material (A572, A992,
  A36, MR250…) ou pela configuração. A altura do perfil do pilar segue o eixo Y da família.
- A viga que chega perto do eixo de um pilar ou de outra viga de aço vai para o eixo dele (o suporte ganha nó). Ligação padrão
  flexível (rótula) ou rígida e base engastada ou rotulada: configuração do projeto, grupo Análise.
- Cada ponta flexível ganha uma `LigacaoMetalica` (chapa de alma proposta pela altura da viga), verificada com o V e o N da ponta
  (códigos LC1–LC9). As barras: MT1–MT9 (NBR 8800:2024). Núcleo: `DEB.Core/Elementos/Metalica`.

## Biblioteca de ligações e famílias PCEB (D49)
- Núcleo: `DEB.Core/Elementos/Metalica/Ligacoes` (`BibliotecaLigacoes`, esquemas, `LigacoesPceb.json`, janela `TelaBibliotecaLigacoes.html`);
  cálculos em `LigacaoChapaAlma`, `LigacaoCantoneira`, `LigacaoChapaTopo`, `LigacaoPlacaBase`.
- Botões: "Biblioteca de ligações" (edita e grava o escritório) e "Modelar ligações" (resumo → aceite → famílias PCEB, grupo "PCEB <id>").
- Famílias: `src/Analise/FamiliasLigacoes.cs` cria "PCEB Chapa" e "PCEB Parafuso" em `%AppData%\PCEBamilias` na primeira vez. Armadilhas:
  com "Sempre vertical" ligado (padrão do modelo genérico) a instância ignora o plano de trabalho; o plano de referência tem de ser
  criado numa vista 3D (numa planta ele é projetado no plano da vista) e com 3 pontos; a API dá normal = corte × (livre − bolha).
  O eixo da análise é a linha de locação (topo da viga): `BarraMetalicaObjeto.DeslocamentoFisico` leva ao centro físico.
- AutoRun: `ligacoes` (imagem da janela) e `analitico` com `SD_ACO=1` (modela as ligações e grava `ligacoes_3d*.png`).

## Ajustes e cargas na janela (D50)
- Seleção de um objeto → "Ajustes do modelo" (o valor vem do Revit; o ajustado fica marcado e "↺" volta) e "Cargas". A análise refaz na
  hora. Ao fechar: ajustes e cargas do PCEB vão para o .rvt (Extensible Storage `PCEB_AjustesAnalise` e `PCEB_CargasAnalise`) e valem em
  toda montagem (`ModeloAnaliticoCommand.Montar`): análise, pilares, vigas, ligações.
- Cargas nativas do Revit (pontual, linear, de área) são lidas e ligadas à peça mais próxima (`CargasRevit.LerNativas`); natureza
  permanente/morta → G, demais → Q. "Enviar cargas do PCEB ao Revit" cria as do PCEB como nativas (casos "PCEB G"/"PCEB Q"), hospedadas
  no elemento analítico da peça (criado e associado se faltar — no Revit 2025 a carga precisa de hospedeiro analítico).


## Consolo, ligações, catálogo, exportação e campos de tensões (D50, itens 3–9)
- **Detalhe do consolo** (`DEB.Core/Analise/Chapas/DetalheConsolo.cs`): aba "2D da peça" com um consolo selecionado — trecho do pilar,
  consolo, aparelho, ponta da viga, Vd/Hd/N da combinação ELU de maior V, biela e tirante (z = 0,9·d) e chapa elástica Q4
  (`ChapaPlana`) com isovalores (teclas 1, 2, x, y, t). Consolos do mesmo pilar e plano entram na mesma chapa (vizinhos).
- **Campos de tensões** (`CampoTensoes.cs`, tecla c): chapa não linear com as armaduras discretas (tirante, costura, verticais do
  detalhamento ou estimadas; pilar com ρ = 1% como hipótese) — concreto sem tração, parábola-retângulo com kc2 e ηfc (linha do CSFM),
  aço elastoplástico; secante com relaxação e convergência por tensões e deformações; carga crescente até a falha (λ ≤ 3). Leva ~8 s
  por consolo (a janela espera).
- **Desenhos das ligações** (`GeometriaLigacao.cs`): sólidos em caixas → vista de topo, frontal, lateral e 3D com os perfis transparentes.
- **Tipologias** (`LigacoesComplementares.cs`): apoio de terça, contraventamento em chapa de nó (Whitmore, K = 0,65), tubo com chapa em
  fenda (Ct; Lw ≥ 1,3·D) e emenda com talas; a biblioteca mostra as resistências (bisseção sobre as verificações). Ainda sem modelagem
  no Revit (o comando avisa).
- **Catálogo de fixadores** (`CatalogoFixadores.cs`): parafusos/porcas/arruelas pelas normas de produto e comprimento comercial pela
  pega; chumbadores de fabricantes em `%AppData%\PCEB\padroes\fixadores.json` (fora do Git, com a fonte de cada linha); a placa de
  base confere a ancoragem escolhida (PB9, PB10).
- **Exportação** (`DEB.Core/Analise/Barras/ExportacaoAnalise.cs`, menu "Exportar…"): OpenSees (script openseespy; grava
  `resultados_<caso>.json`), CalculiX (.inp com B32R subdivididas, seção retangular equivalente; rótula solta as três rotações; P1/P2 =
  pressão na face) e SAP2000 (.s2k). As barras são divididas nos pontos de carga. Os testes rodam OpenSees (igual ao PCEB) e
  CalculiX (~2%) quando `%AppData%\PCEB\mef.txt` existe; o `analitico` exporta e roda os dois no modelo do Revit.

## Unidades e parâmetros (D50)
- A janela mostra e recebe nas unidades do grupo Unidades da configuração do projeto (`UnidadesAnalise`): força, momento, cargas
  lineares e de superfície, momento distribuído das lajes, rigidez das molas e tensão. O cálculo continua em kN e cm; os textos do
  núcleo (dimensionamento, memória, verificações) têm os valores com kN convertidos antes de chegar à página.
- Lajes: `PCEB_Carga permanente (kN/m²)`, `PCEB_Carga variável (kN/m²)`, `PCEB_Peso próprio (kN/m²)` e `PCEB_Tipo de laje` são
  compartilhados (GUIDs `6a1e3f40-…-9b0c1d2e00NN`, `Analise/ParametrosAnalise.cs`), lidos pelo GUID ou, nos modelos antigos, pelo nome.
