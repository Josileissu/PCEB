# Análise por objetos e janela do modelo de análise (D44, D46)

O modelo de análise é do PCEB (decisão de 24/11/2026: fora do Revit). O Revit é só lido; nada é escrito no modelo.
Botão **Pré-moldados › Modelo de análise** (`Commands/ModeloAnaliticoCommand.cs`).

## Do Revit para os objetos (`Analise/EstruturaRevit.cs`)
| No Revit | Objeto (núcleo, `DEB.Core/Analise/Objetos`) | No modelo de barras |
|---|---|---|
| Pilar estrutural (levantamento dos pré-moldados) | `PilarObjeto` | barras no eixo (CG), nós nos níveis dos consolos/vigas/lajes, engaste no topo do cálice (embutimento) |
| Viga com consolo aninhado apoiado no pilar | `VigaObjeto` + `LigacaoConsolo` | barra no CG; vínculo rígido do eixo do pilar ao topo do consolo; mola do aparelho (Kh = G·A/t, Kv = 5·G·S²·A/t) |
| Outra viga (linha de locação reta) | `VigaObjeto` + `LigacaoPilarViga` | vínculo rígido ao pilar onde a ponta cai: monolítica (moldada no local) ou articulada (pré-moldada) |
| Piso estrutural | `LajeObjeto` | grelha (analogia de Hambly: I = b·h³/12, J = b·h³/6), cargas por m² nos nós, nós ligados às vigas e pilares embaixo |

- Seções: da geometria levantada (`PropriedadesSecao`: área, inércias, produto de inércia, torção por Prandtl), não das
  propriedades analíticas do Revit.
- Cargas: vigas pelos parâmetros `PCEB_Carga…` (D40); lajes por `PCEB_Carga permanente (kN/m²)` e `PCEB_Carga variável (kN/m²)`
  (por nome) ou pela configuração (`acoes.lajeG`, `acoes.lajeQ`); peso próprio pelo material.
- Sem aparelho de apoio na família: elastômero padrão da configuração (10 cm × largura da viga − 4 × 1 cm), com aviso.
- Viga apoiada em viga: ligação articulada no ponto de apoio (a viga de suporte ganha um nó ali).
- Laje apoiada direto no pilar: o nó da grelha sobre o pilar é preso a ele — o momento negativo ali é de pico (singularidade da
  grelha); para armar, use o valor na face do pilar.
- Ainda não: aberturas de laje, vento, combinações completas
  (há um conjunto mínimo provisório — o usuário deixou as combinações para depois).

## Solver (`DEB.Core/Analise/Barras`)
Pórtico espacial linear: rótulas por condensação, molas, vínculos rígidos mestre-escravo, apoios com mola, perfil (skyline) com
Cuthill-McKee reverso. Conferido com fórmulas fechadas, com o OpenSees e com o sectionproperties (scripts em
`tests/DEB.Core.Tests/Gabaritos`). Deformada ao longo da barra exata (Hermite + solução particular).

## Dimensionamento (`DEB.Core/Analise/Dimensionamento`)
Envoltória ELU por barra e uma verificação por tipo de objeto: viga (flexão, bielas, estribos, flecha estimada), pilar (Nd, Md,
momento mínimo, esbeltez; flexo-compressão oblíqua = etapa 3, "Conferir"), consolo (cálculo do núcleo com a armadura do
detalhamento), laje (armadura por metro, flecha). Cada peça sai com situação, resumo, verificações e memória.

## Janela (`DEB.Core/Analise/Objetos/TelaAnalise.html` + `VisualizacaoAnalise`; WebView2 do Revit)
3D (girar, mover, zoom, vistas prontas) com modos Modelo, Deformada, Diagrama (momentos/cortantes/normal/torção do lado
tracionado), Mapa de cores e Situação; **2D da peça** (M, V, N do resultado + envoltória ELU tracejada); **Laje · isovalores**
(mx, my, mxy, flecha). Lista de objetos com a situação; painel com esforços nas pontas, forças no aparelho, reações e o
dimensionamento (verificações e memória).

## Testar
AutoRun `analitico` (sem gravar): grava `janela_<vista>.png` de seis vistas e o resumo do dimensionamento com a memória.
`SD_LAJE=1` cria um piso de 12 cm sobre as vigas do modelo aberto, para exercitar as lajes. Prévia fora do Revit: o teste
`EstruturaObjetosTests` com `PCEB_PREVIA=<pasta>` grava `previa_analise.html` e `previa_laje.html` (abrir no navegador).
