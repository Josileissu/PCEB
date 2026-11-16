# Paridade do pilar pré-moldado: Revit × CAD (etapa 3 da O-2)

Teste: `tests/DEB.Core.Tests/ParidadePilarTests.cs` (`--filter ParidadePilar`). Mesma peça e mesma armadura dos dois lados:
pilar 6 m (1 m embutido), seções 40×40 e 50×50, sem consolo / um consolo (+x) / consolos opostos (±x), longitudinal ø12,5
e ø16; consolo 40×30×25 com topo a 4 m; armadura do Revit = `ColumnRebarSpec.Default`. O CAD recebe um tipo de consolo
com a armadura da especificação do Revit. Comprimentos dos dois lados pelo núcleo (cm, arredondados como na relação do aço).

Caminhos: Revit = `src/DEB.Core/Templates/PilarPreMoldado/Modelo3D/ColumnRebarLayout.cs` (o plugin só converte para o
Revit em `src/DEB.Revit/src/Precast/RebarFactory.cs`); CAD = `src/DEB.Core/Templates/PilarPreMoldado/` (abaixo `Arm` =
`ArmadurasPilar.cs`, `Cons` = `ConsolosPilar.cs`, `Lis` = `ListasArmaduraPilar.cs`, `Lay` = `ColumnRebarLayout.cs`).

## Mapa das regras

| Regra | Revit | CAD / núcleo | Mesmo número? |
|---|---|---|---|
| Posição das longitudinais na seção | `Lay:50` (cobrimento + φt + φ/2, faces divididas por igual) | `SecaoBarras.Posicoes` / `Lis:214` | sim |
| Cobrimento das pontas das longitudinais | `Lay:56` | `Arm:121` (`CobTopo`/`CobBase`) | sim |
| Estribo: medidas | `Lay:45` (eixo a c + φt/2) → externas b − 2c | `Arm:193` (externas b − 2c) | sim |
| Estribo: ganchos | 135° (`Lay:78`; tipo de gancho do Revit `RebarFactory.cs:299`) | 90° (`Arm:386`, `GeradorEstribo.FechadoPadrao`) | não (+2 cm no CAD) |
| Estribo: zonas e quantidade | `Lay:363` (início a 5 cm das pontas, menor espaçamento onde as zonas se sobrepõem, sem repetir na divisa) | `Lis:141` + faixa por zona (`CalculoFaixa.Quantidade`, piso + 1) | quase (CAD +1 em divisa) |
| Grampo: medida e ganchos | `Lay:84` (barras a mais de 20φt do canto), 135° | `Arm:399` (um por direção, b − 2c), 135° | sim |
| Tirante do consolo (simples) | `Lay:212` (para nas longitudinais da face oposta; perna interna no eixo) | `Cons:522`, `Cons:696` (vai até o cobrimento; perna externa) | não (−2 cm no Revit) |
| Tirante (consolos opostos) | um L por consolo | `Cons:687` reto passante (2a + b − 2c) | não |
| Barra soldada | `Lay:241` (b − 2c) | `Cons:734` (b − 2c) | sim |
| Costura | `Lay:333` (2/3 d, d = h − c; até as longitudinais; b − 2c; 135°) | `Cons:607`, `Cons:822` (2/3 d, d = h − c − φtir/2; até o cobrimento; b − 2c − 2φvert; 90°) | não |
| Estribo vertical do consolo | `Lay:254` (abaixo do tirante; 135°) | `Cons:808` (h − 2c; 90°) | não (−4 cm no Revit) |
| Armadura inferior do consolo | `Lay:285` (U, pernas para cima) | `Cons:529` (perna na ponta + ancoragem + retorno) | não |
| Pino do consolo | `Lay:309` (medidas no eixo, no meio do balanço) | `Cons:844` (medidas externas, no centro da almofada) | não (+2 cm no Revit) |
| Alças de içamento | `Lay:166` (barra ø16 em U, 0,2 H e 0,8 H, pernas ao longo do pilar) | `ItensPilar.cs:75` (cordoalha, simétrica ao CG, pernas até a face oposta) | não comparável (fora do teste) |
| Comprimento real (dobras, ganchos, arredondamento) | `ComprimentoPorEixo` sobre o eixo | `MedidaBarra.Unitario` sobre a barra desenhada | mesma regra do núcleo |

## Divergências (estado travado no teste, cm)

| Medida | 40 um | 40 opostos | 50 um | 50 opostos | Causa |
|---|---|---|---|---|---|
| Estribo: comprimento (Revit / CAD) | 153 / 155 | 153 / 155 | 193 / 195 | 193 / 195 | ganchos 135° × 90° |
| Estribo: quantidade, sem consolo | 45 / 46 | — | 45 / 46 | — | CAD repete o estribo na divisa corrente/topo |
| Tirante: comprimento | 95 / 97 | 95 / 85 | 105 / 107 | 105 / 95 | para nas longitudinais × vai ao cobrimento; opostos: L × passante |
| Tirante: quantidade | 3 / 3 | 6 / 3 | 3 / 3 | 6 / 3 | opostos: um por consolo × um passante |
| Costura: comprimento | 201 / 205 | 200 / 255 | 220 / 225 | 221 / 275 | ver mapa; opostos: uma por consolo × passante |
| Costura: quantidade | 3 / 3 | 6 / 3 | 3 / 3 | 6 / 3 | opostos |
| Estribo vertical do consolo | 131 / 135 | 131 / 135 | 131 / 135 | 131 / 135 | abaixo do tirante e 135° × h − 2c e 90° |
| Armadura inferior do consolo | 92 / 98 | 91 / 79 | 101 / 98 | 102 / 79 | formas diferentes |
| Pino do consolo | 76 / 74 | 76 / 74 | 76 / 74 | 76 / 74 | medidas no eixo × externas |

Iguais em todos os casos: longitudinais (quantidade, posições, 595), grampo (47 e 57), quantidade de estribos e grampos com
consolo, barra soldada (35).

## Revit × núcleo na mesma barra (fora do teste)
`RebarFactory.LogLengthComparison` registra o comprimento que a API do Revit mede (`REBAR_ELEM_LENGTH`) ao lado do comprimento do
núcleo para a mesma geometria. Teste real de 05/11 (P2-6): estribo +6,8, grampo +2,0, costura +9,0, tirante −0,9 (núcleo −
Revit). Vem de como o Revit mede a barra (eixo, extensão do tipo de gancho do projeto) × a regra do CAD (medidas externas,
ganchos e passagens da NBR, arredondamento): é próprio do Revit e só se mede com o Revit aberto. As tabelas e o peso do Revit
continuam pelo comprimento do Revit; trocar para o do núcleo é decisão do dono.
