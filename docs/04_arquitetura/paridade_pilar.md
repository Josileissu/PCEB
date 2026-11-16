# Paridade do pilar pré-moldado: Revit × CAD (etapa 3 da O-2)

Teste: `tests/DEB.Core.Tests/ParidadePilarTests.cs` (`--filter ParidadePilar`). Mesma peça e mesma armadura dos dois lados:
pilar 6 m (1 m embutido), seções 40×40 e 50×50, sem consolo / um consolo (+x) / consolos opostos (±x), longitudinal ø12,5
e ø16; consolo 40×30×25 com topo a 4 m; armadura do Revit = `ColumnRebarSpec.Default`. O CAD recebe um tipo de consolo
com a armadura da especificação do Revit. Comprimentos dos dois lados pelo núcleo (cm, arredondados como na relação do aço).

Caminhos: Revit = `src/DEB.Core/Templates/PilarPreMoldado/Modelo3D/ColumnRebarLayout.cs` (o plugin só converte para o
Revit em `src/DEB.Revit/src/Precast/RebarFactory.cs`); CAD = `src/DEB.Core/Templates/PilarPreMoldado/` (abaixo `Arm` =
`ArmadurasPilar.cs`, `Cons` = `ConsolosPilar.cs`, `Lis` = `ListasArmaduraPilar.cs`, `Itens` = `ItensPilar.cs`,
`Lay` = `ColumnRebarLayout.cs`).

Decisões do dono de 16/11/2026 (D30 em `00_escopo/escopo_organizado.md`) aplicadas na branch `feat/pilar-regras-unificadas`.

## Mapa das regras

| Regra | Função do núcleo (CAD e Revit) | Mesmo número? |
|---|---|---|
| Posição das longitudinais na seção | `SecaoBarras.Posicoes` | sim |
| Cobrimento das pontas das longitudinais | `Lay` (`LongEndCoverMm`) / `Arm` (`CobTopo`/`CobBase`) | sim |
| Estribo: medidas externas b − 2c | `Arm.Estribo` / `ComprimentoPorEixo` | sim |
| Ganchos do estribo, da costura e do estribo vertical: 135° (D30-1) | `GeradorEstribo.FechadoPadrao` com gancho de 45° nos dois | sim |
| Estribo na divisa entre zonas: não repete (D30-2) | `TemplatePilarPreMoldado.EstribosNaZona` / `RepeteNaDivisa` (CAD: `FimDaFaixa`; Revit: `StirrupRuns`) | quantidade sim; zonas não (ver abaixo) |
| Grampo: medida e ganchos | `Lay` (barras a mais de 20φt do canto) / `Arm.Grampo`, 135° | sim |
| Tirante e costura: fim dentro do pilar, por dentro das longitudinais da face oposta; um por consolo também nos opostos (D30-3) | `BarrasConsolo.FimNoPilar` / `Horizontal` | tirante sim; costura só no comprimento ao longo do balanço (largura diverge) |
| Perna do tirante (medida externa) e ponta livre no cobrimento | `Lay` converte para o eixo (D7) | sim |
| Barra soldada | b − 2c nos dois | sim |
| Estribo vertical do consolo | `Lay` (abaixo do tirante) × `Cons` (h − 2c); ganchos 135° nos dois | não (posição não decidida) |
| Armadura inferior do consolo | `Lay` (U, pernas para cima) × `Cons` (perna na ponta + ancoragem + retorno) | não (não decidida) |
| Pino do consolo | medidas externas nos dois; `Lay` no meio do balanço, `Cons` no centro da almofada | comprimento sim; posição não |
| Alças: alturas simétricas ao CG, (50 − 20) % de L dele (D30-5) | `TemplatePilarPreMoldado.AlturaCG` + `PosicaoAlcas` | sim |
| Alças: pernas até a face oposta menos o cobrimento, com pé (D30-5) | `TemplatePilarPreMoldado.EmbutimentoAlca` | sim |
| Comprimento real (dobras, ganchos, arredondamento) | `ComprimentoPorEixo` (eixo) / `MedidaBarra.Unitario` (barra desenhada) — mesma regra | sim |

## Medidas no teste (cm, Revit / CAD)

| Medida | 40 sem | 40 um | 40 opostos | 50 sem | 50 um | 50 opostos |
|---|---|---|---|---|---|---|
| Estribo: comprimento | 153 / 153 | 153 / 153 | 153 / 153 | 193 / 193 | 193 / 193 | 193 / 193 |
| Estribo: quantidade | 45 / 45 | 51 / 51 | 51 / 51 | 45 / 45 | 51 / 51 | 51 / 51 |
| Grampo: quantidade | 90 / 90 | 102 / 102 | 102 / 102 | 90 / 90 | 102 / 102 | 102 / 102 |
| Tirante: comprimento | — | 95 / 95 | 95 / 95 | — | 105 / 105 | 105 / 105 |
| Tirante: quantidade | — | 3 / 3 | 6 / 6 | — | 3 / 3 | 6 / 6 |
| Costura: comprimento | — | 201 / 198 | 200 / 197 | — | 220 / 217 | 221 / 218 |
| Costura: quantidade | — | 3 / 3 | 6 / 6 | — | 3 / 3 | 6 / 6 |
| Estribo vertical do consolo | — | 131 / 133 | 131 / 133 | — | 131 / 133 | 131 / 133 |
| Armadura inferior do consolo | — | 92 / 98 | 91 / 79 | — | 101 / 98 | 102 / 79 |
| Alças: alturas | 120 e 480 | 122,6 e 482,6 | 125 e 485 | 120 e 480 | 121,7 e 481,7 | 123,3 e 483,3 |
| Alças: embutimento das pernas | 37,5 | 37,5 | 37,5 | 47,5 | 47,5 | 47,5 |

Alturas e embutimento das alças iguais nos dois lados. Iguais também: longitudinais (quantidade, posições, 595), grampo
(47 e 57), barra soldada (35), pino (74).

## O que ainda diverge

| Divergência | Causa | Recomendação |
|---|---|---|
| Largura da costura (−3 cm no CAD) | Revit b − 2c, no mesmo lugar dos estribos verticais do consolo (as barras se cruzam no mesmo plano); CAD b − 2c − 2φvert, por dentro deles. A D30 tratou do fim dentro do pilar, não da largura | Adotar a do CAD nos dois (sem colisão) — decisão do dono |
| Zonas de estribos (alturas, não quantidades) | Revit começa a 5 cm das pontas (`StirrupEndOffsetMm`), CAD no cobrimento das longitudinais (2,5); zonas sobrepostas: Revit usa o menor espaçamento, CAD a primeira da lista. Por isso o Revit não passou a usar `LerZonas` | Decidir o início e a regra de sobreposição; depois o Revit chama `LerZonas` |
| Estribo vertical do consolo (+2 cm no CAD) | Revit abaixo do tirante (h − 2c − φtir); CAD h − 2c | não decidida (só o gancho mudou) |
| Armadura inferior do consolo | formas diferentes | não decidida |
| Consolos opostos: tirantes e costuras dos dois consolos no mesmo lugar | um por consolo nos dois lados (D30-3): no pilar, o L de um consolo e o do outro ocupam a mesma altura e as mesmas posições ao longo da face | decisão do dono (deslocar um conjunto, ou voltar ao passante) |
| Alças: material e bitola | CAD cordoalha CP-190 ø12,7 (2 por alça); Revit barra ø16 CA-50 (tipo de barra do Revit) | decisão do dono |
| Alças: face | CAD pelo parâmetro `AlcaFace`; Revit pela regra `LiftFace` (face com consolos para cima) | decisão do dono |
| Alças: perto de consolo | CAD avisa; Revit desloca a alça o mínimo para fora do consolo | — |
| Alças: topo do laço | CAD semicírculo; Revit dois cantos dobrados (o Revit arredonda pelo pino) | — |
| Pino: posição ao longo do balanço | meio do balanço × centro da almofada | — |
| Etiqueta da alça no Revit (família de etiqueta) | a etiqueta da família do usuário lê o comprimento do Revit (C=149 × núcleo 150) | mudar a família de etiqueta para ler `SD_Comprimento (núcleo)` |

## Revit × núcleo na mesma barra
`RebarFactory.LogLengthComparison` registra o comprimento que a API do Revit mede (`REBAR_ELEM_LENGTH`) ao lado do comprimento do
núcleo. Teste de 16/11 (Revit 2026, `Teste_PM.rvt`): estribo +6,8, grampo +2,0, costura +9,0, tirante −0,4, estribo vertical
+8,3, alça +0,8 a +1,3, pino −1,4 a +1,1 (núcleo − Revit). **Desde a D30 as tabelas, o peso e os rótulos de texto do Revit usam o
comprimento do núcleo**: parâmetros `SD_Comprimento (núcleo)` e `SD_Comprimento total (núcleo)` (GUIDs `…5e03`/`…5e04`), gravados
quando o plugin cria a barra. São valores gravados: barra editada à mão no Revit só se atualiza rodando o comando de novo.
