# Fluxo análise → dimensionamento → detalhamento

## Matriz etapa × programa
| Etapa | Revit | Robot | SAP2000 | TQS | Eberick | CYPE 3D | Open source | StructuralDetailer |
|---|---|---|---|---|---|---|---|---|
| Modelo físico / geometria | ● | ○ | ○ | ● | ● | ○ | IfcOpenShell | lê o modelo do Revit (pilar, consolos aninhados) |
| Modelo analítico | ● (AnalyticalMember) | ● | ● | ● | ● | ● | PyNite, OpenSees | — |
| Cargas e combinações | ○ | ● | ● | ● NBR | ● NBR | ● | (fazer) | — |
| Esforços | — | ● | ● | ● | ● | ● | PyNite, OpenSees | — |
| Dimensionamento NBR 6118/9062 | — | ○ (armadura necessária) | ✕ (sem NBR) | ● | ● | ● | concreteproperties (base) | **verificações** de detalhamento (Ø mín., espaçamentos, consolo: costura ≥ 0,4 tirante) |
| Detalhamento / armaduras | ● (Rebar) | ○ | ✕ | ● | ● | ○ | — | **●** barras, marcas, assemblies, pranchas PP127 |
| Pranchas | ● | ✕ | ✕ | ● | ● | ○ | — | **●** padrões de apresentação |

● faz bem · ○ parcial · ✕ não faz — a confirmar ao estudar cada um.

## O que reaproveitar do StructuralDetailer
- `Precast/PrecastColumn.cs` — levantamento da peça (eixo local, consolos) → vira a origem do **modelo analítico**.
- `Precast/ColumnRebarSpec.cs` — armadura escolhida + verificações → ponto natural para entrar o **dimensionamento**
  (hoje só verifica regras construtivas; falta As,nec a partir dos esforços).
- `Precast/ColumnRebarLayout.cs` — geometria das barras independente do Revit → serve a qualquer motor.
- `Presentation/*` — pranchas por padrão; independem de onde vieram os esforços.

## Lacunas (o que estudar)
1. **Esforços**: de onde vêm? (Robot via integração, motor próprio, ou importar de TQS/Eberick).
2. **Situações transitórias do pré-moldado**: desforma, içamento, transporte, montagem — pouco cobertas
   pelos programas gerais; PREFAB do TQS cobre parte.
3. **Pilar pré-moldado**: engaste no cálice, 2ª ordem (pilar em balanço), consolo (biela-tirante NBR 9062).
4. **Dimensionamento → detalhamento automático**: As,nec → escolha de barras → `ColumnRebarSpec`.

## Casos-teste propostos (casos-teste/)
- CT01 — pilar pré-moldado em balanço, engastado no cálice, com 1 consolo (o PP127 da prancha-modelo).
- CT02 — viga pré-moldada biapoiada (situação final + içamento).
- CT03 — pórtico plano simples para comparar motores (Robot × SAP2000 × PyNite × OpenSees × FTOOL).
