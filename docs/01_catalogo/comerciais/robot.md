# Autodesk Robot Structural Analysis Professional

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| Autodesk (origem polonesa, ex-RoboBAT) | comercial (assinatura Autodesk) | Windows | ✓ **2025** e **2026** (`C:\Program Files\Autodesk\Robot Structural Analysis Professional 202x`, 1,1 GB); SDK em `...\SDK` ✓ |

Levantamento de 05/11/2026 pelos arquivos instalados, `Cfg\R_NORM.CFG` (normas), configurações de concreto (`*.rccx`),
`Robot API.chm` (12.360 páginas) e o `Required Reinforcement SDK`; o programa não foi aberto.

## Arquitetura e tecnologia ✓
- Interface **MFC** (Win32, antiga); ~1.850 DLLs, quase todas nativas; módulos de norma em DLLs (`d*.dll` aço,
  configurações `*.rccx` concreto).
- Geometria: **RealDWG/ObjectARX**, **ACIS**, **CGAL**; cálculo com **Intel TBB** e OpenMP.
- **API COM** (`RobotOM`, `Interop.RobotOM.dll`): 946 interfaces/tipos; **add-ins** em C#/VB.NET (exemplos `MyAddin`).

## Modelagem e análise ✓ (pela API)
- Barras (seções paramétricas, de concreto e especiais, liberações, excentricidades, imperfeições), cabos, painéis/
  cascas com **malha** (Delaunay, Coons) e sólidos.
- Análises: linear, **não linear** (rótulas plásticas com curvas, links não lineares: gap, gancho, plástico,
  bilinear), **flambagem**, modal, **espectral**, **sísmica** por normas, **histórico no tempo** (Newmark, HHT,
  decomposição modal, não linear), cargas móveis (inclusive **NBR 7188** ✓), **simulação de vento** em túnel virtual
  (`IRobotWindLoadsSimulationEngine`), neve e vento por normas europeias.
- Integração oficial **Revit ↔ Robot** (modelo analítico de ida e volta).

## Dimensionamento ✓
- **Aço**: muitas normas, inclusive **ABNT NBR 8800:2008** ✓ (`dnbr.dll`); ligações metálicas (`IRJoint*`).
- **Concreto (armadura necessária)**: ACI, BAEL, BS, CSA, EHE, Eurocode 2 e anexos nacionais, SNiP, GB, IS, NEN,
  PN, entre outras — **sem NBR 6118** ✓ (nenhum `*.rccx` brasileiro).
- **Required Reinforcement SDK** (C++): permite **acrescentar uma norma própria** de armadura necessária aos módulos
  "lajes/paredes" e "vigas/pilares"; a norma aparece na lista do Robot como as nativas (exemplos e modelo genérico, EC2).

## Detalhamento ✓
- Módulos de **armadura fornecida e desenho** de elementos de concreto (`IRConcr*`): **vigas** (vãos, apoios, cargas,
  fissuração), **pilares** (flambagem), **sapatas** e **sapatas corridas**, **lajes**, **paredes** (com aberturas),
  **vigas-parede**; barras e grupos de barras no desenho (`IRConcrDrawing`). Pranchas e tabelas (`IRobotTable`, relatórios).

## API / automação ✓
- COM: aplicação, projeto, estrutura (nós, barras, painéis, malha), casos e **combinações por norma**, cálculo
  (`IRobotCalcEngine`), resultados (barras, nós, painéis, flambagem, histórico no tempo, armadura de elementos
  finitos), tabelas, relatórios, preferências; add-ins dentro do Robot.
- SDK com guia de início, tutorial, exemplos em Excel (`Excel_Formulas.xlsm`, geração de estruturas), aço e silos.

## Pré-moldados
- Nada específico; análise em fases é limitada (sem "staged construction" como o SAP2000).

## O que aproveitar para o nosso programa
1. **Required Reinforcement SDK**: caminho para oferecer **NBR 6118 dentro do Robot** usando o nosso núcleo de
   dimensionamento — o Robot faz a análise e o PCEB fornece a norma. Para o escritório, que usa Revit + Robot, é uma
   ponte natural (fase de análise).
2. **Revit ↔ Robot** já existe: o modelo analítico do Revit pode ir para o Robot e voltar; nosso detalhamento (D19)
   continua no Revit.
3. API COM com **combinações por norma** e **resultados de armadura de elementos finitos** — útil para comparar lajes e
   paredes com o nosso cálculo.
4. **NBR 8800** e **NBR 7188** prontas — referência para conferir o nosso dimensionamento de aço (fase 4) e pontes.

## A confirmar
- Se o escritório usa o Robot hoje e com qual fluxo (Revit → Robot → Revit?).
- Esforço real do Required Reinforcement SDK (C++, compilação para a versão do Robot).
