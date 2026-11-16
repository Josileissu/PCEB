# Paridade do pilar pré-moldado: Revit × CAD (etapa 3 da O-2)

Teste: `tests/DEB.Core.Tests/ParidadePilarTests.cs` (`--filter ParidadePilar`). Mesma peça e mesma armadura dos dois lados:
pilar 6 m (1 m embutido), seções 40×40 e 50×50, sem consolo / um consolo (+x) / consolos opostos (±x), longitudinal ø12,5
e ø16; consolo 40×30×25 com topo a 4 m; armadura do Revit = `ColumnRebarSpec.Default`. O CAD recebe um tipo de consolo
com a armadura da especificação do Revit. Comprimentos dos dois lados pelo núcleo (cm, arredondados como na relação do aço).

Caminhos: Revit = `src/DEB.Core/Templates/PilarPreMoldado/Modelo3D/ColumnRebarLayout.cs` (o plugin só converte para o
Revit em `src/DEB.Revit/src/Precast/RebarFactory.cs`); CAD = `src/DEB.Core/Templates/PilarPreMoldado/`.

Decisões do dono de 16/11/2026 (D30 em `00_escopo/escopo_organizado.md`, com a revisão do mesmo dia) aplicadas na branch
`feat/pilar-regras-unificadas`.

## Mapa das regras

| Regra | Função do núcleo (CAD e Revit) | Mesmo número? |
|---|---|---|
| Posição das longitudinais na seção | `SecaoBarras.Posicoes` | sim |
| Estribo: medidas externas b − 2c; ganchos 135° (estribo, costura, estribo vertical) | `GeradorEstribo.FechadoPadrao` com gancho de 45° | sim |
| Estribos: início no cobrimento das pontas; sem repetir na divisa entre zonas | `KitDesenho.EstribosNaZona` / `RepeteNaDivisa` / `FimSemRepetir` (também na viga) | sim (alturas e quantidades) |
| Estribos: zonas (menor espaçamento onde se sobrepõem; corrente nos vãos) | `TemplatePilarPreMoldado.MontarZonas` (CAD em `LerZonas`, Revit em `StirrupRuns`) | sim |
| Grampo | `Lay` / `Arm.Grampo`, 135° | sim |
| Consolos opostos em nível = duplo (tolerância 0,6 cm) | `BarrasConsolo.ParesEmNivel` | sim |
| Tirante e costura: sozinho até por dentro das longitudinais; duplo passante | `BarrasConsolo.FimNoPilar` / `Horizontal` / `Passante` | sim |
| Largura da costura: por dentro dos estribos verticais | `BarrasConsolo.LarguraCostura` | sim |
| Barra soldada, pino (comprimento) | b − 2c; medidas externas | sim |
| Estribo vertical e armadura inferior do consolo | **definidos pelo usuário** (abaixo) | não unificados |
| Alças: alturas (CG ± 30 % de L) e embutimento (por dentro das longitudinais da face oposta) | `AlturaCG` + `PosicaoAlcas`; `EmbutimentoAlca` | sim |
| Alças: cordoalha CP-190 ø12,7, topo em semicírculo, 2 por ponto, face escolhida (padrão sul) | `ColumnRebarLayout.LiftSteel`, `BarDef.ArcMid`, `LiftLoops`, `LiftFaceOf` | sim |
| Comprimento real (dobras, ganchos, arredondamento) | `ComprimentoPorEixo` / `MedidaBarra.Unitario` | mesma regra |

## Medidas no teste (cm, Revit / CAD)

| Medida | 40 sem | 40 um | 40 opostos | 50 sem | 50 um | 50 opostos |
|---|---|---|---|---|---|---|
| Estribo: comprimento | 153 / 153 | 153 / 153 | 153 / 153 | 193 / 193 | 193 / 193 | 193 / 193 |
| Estribo: quantidade | 45 / 45 | 51 / 51 | 51 / 51 | 45 / 45 | 51 / 51 | 51 / 51 |
| Estribo: maior diferença de altura | 0 | 0 | 0 | 0 | 0 | 0 |
| Tirante: comprimento | — | 95 / 95 | 85 / 85 | — | 105 / 105 | 95 / 95 |
| Tirante: quantidade | — | 3 / 3 | 3 / 3 | — | 3 / 3 | 3 / 3 |
| Costura: comprimento | — | 198 / 198 | 252 / 252 | — | 217 / 217 | 272 / 272 |
| Costura: quantidade | — | 3 / 3 | 3 / 3 | — | 3 / 3 | 3 / 3 |
| Estribo vertical do consolo (usuário) | — | 131 / 133 | 131 / 133 | — | 131 / 133 | 131 / 133 |
| Armadura inferior do consolo (usuário) | — | 92 / 98 | 91 / 79 | — | 101 / 98 | 102 / 79 |
| Alças: alturas | 120 e 480 | 122,6 e 482,6 | 125 e 485 | 120 e 480 | 121,7 e 481,7 | 123,3 e 483,3 |
| Alças: embutimento | 35,75 | 35,75 | 35,4 | 45,4 | 45,4 | 45,75 |
| Alças: comprimento | 147 / 147 | 147 / 147 | 147 / 147 | 167 / 167 | 167 / 167 | 167 / 167 |
| Alças: face e cordoalhas por ponto | 2 e 2 | 2 e 2 | 2 e 2 | 2 e 2 | 2 e 2 | 2 e 2 |

Zonas sobrepostas (teste `ZonasSobrepostas`, base 4 m c/10 e consolo 3,5–4,2 m c/7,5): c/7,5 nos dois, mesmas quantidades e
alturas. Iguais também: longitudinais (quantidade, posições, 595), grampo (47 e 57, 90 e 102), barra soldada (35), pino (74).

## Definidos pelo usuário (não são regra a unificar)
- **Estribo vertical do consolo** — CAD: tipo de consolo do catálogo (ARMCONSOLOS), `VertN` e `VertPhi`; a altura é h − 2c.
  Revit: especificação do grupo de pilares, "Estribos verticais: quantidade / bitola"; a altura vai do tirante ao fundo.
- **Armadura inferior do consolo** — CAD: `InfN`, `InfPhi`, `InfAnc` (ancoragem no pilar), `InfRetorno` e `InfDescida`
  (gancho de cima); forma com perna na ponta, trecho para dentro do pilar e retorno. Revit: "Armadura inferior: nº de
  barras / bitola"; forma em U com as pernas para cima.
- **Falta no Revit** (entradas que o CAD tem): ancoragem, retorno e descida da armadura inferior; armadura por tipo de consolo
  (o Revit tem uma especificação por grupo de pilares, igual para todos os consolos); almofada (`AlmA`/`AlmB`/`AlmE`), que
  no CAD posiciona os pinos (no Revit o pino fica no meio do balanço).

## O que ainda diverge

| Divergência | Causa | Recomendação |
|---|---|---|
| Alças: perto de consolo | CAD avisa; Revit desloca a alça o mínimo | — |
| Alças: distância entre as 2 cordoalhas do ponto | só o Revit posiciona (LiftSpacingMm, 20 cm); o CAD só dá a quantidade | — |
| Pino: posição ao longo do balanço | meio do balanço × centro da almofada | — |
| Etiqueta da alça no Revit | a família de etiqueta do usuário lê o comprimento do Revit | mudar a família para ler `SD_Comprimento (núcleo)` |
| Duplo do CAD com deslocamento | o template espelha o par pelo centro (face 3 em −desl): com desl ≠ 0 os dois consolos não ficam alinhados e não viram duplo automático | — |

## Revit × núcleo na mesma barra
`RebarFactory.LogLengthComparison` registra o comprimento que a API do Revit mede (`REBAR_ELEM_LENGTH`) ao lado do comprimento do
núcleo. Desde a D30 as tabelas, o peso e os rótulos de texto do Revit usam o comprimento do núcleo: parâmetros
`SD_Comprimento (núcleo)` e `SD_Comprimento total (núcleo)` (GUIDs `…5e03`/`…5e04`), gravados quando o plugin cria a barra.
São valores gravados: barra editada à mão no Revit só se atualiza rodando o comando de novo.
