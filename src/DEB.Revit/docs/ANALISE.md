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

