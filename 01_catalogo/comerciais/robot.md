# Autodesk Robot Structural Analysis Professional

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| Autodesk | comercial | Windows | ✓ 2025 e 2026.0.1, com SDK e extensão de integração com o Revit |

## Modelagem
- Barras, cascas, sólidos; geradores de estruturas; recebe o modelo analítico do Revit.

## Análise
- Linear, não linear, P-Δ, modal, espectral, time-history, flambagem, cargas móveis, construção em fases.

## Dimensionamento
- Concreto armado: armadura **necessária** e **adotada** para vigas, pilares, lajes, fundações; aço; madeira.
  Normas Eurocode, ACI, BAEL e outras (NBR a confirmar).

## Detalhamento
- Desenhos de armaduras de peças (módulos RC), pouco usados no Brasil.

## Fabricação / CAM
- Não.

## Documentação
- Relatórios, tabelas, desenhos simples.

## Importa / exporta
- `.rtd` (binário), `.str` (texto), IFC, DXF, **integração Revit** (ida e volta do modelo analítico e armadura) ✓.

## API / automação
- **SDK** ✓ (`...\2025\SDK`): `Robot API.pdf/.chm`, `Getting Started Guide Robot API.pdf`,
  `How To Create Add-In extension.pdf`, **Required Reinforcement SDK** ✓, exemplos ✓.
  COM `Interop.RobotOM.dll` ✓ — usável de .NET (e do nosso plugin Revit).

## Pré-moldados
- Genérico; sem fluxo de pré-moldados.

## O que aproveitar
- Caminho mais curto **Revit → análise**: a integração oficial já existe e está instalada.
- Required Reinforcement SDK: estudar como expõe As,nec (formato que nosso dimensionamento poderia imitar).

## A confirmar
- Normas brasileiras disponíveis; ler a documentação do SDK.
