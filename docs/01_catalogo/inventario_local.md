# Inventário local (levantado em 03/11/2026)

## Programas estruturais instalados

| Programa | Versão | Etapas | Caminho | API / automação |
|---|---|---|---|---|
| Autodesk Revit | 2025 (25.0.2) e 2026 (26.0.4) | modelo, detalhamento | `C:\Program Files\Autodesk\Revit 202x` | API .NET (já usada no StructuralDetailer); modelo analítico (`AnalyticalMember`, `AnalyticalPanel`) |
| Robot Structural Analysis Professional | 2025 e 2026.0.1 | análise, dimensionamento (armadura necessária) | `C:\Program Files\Autodesk\Robot Structural Analysis Professional 202x` | **SDK instalado** (`...\2025\SDK`: `Robot API.pdf/.chm`, `Getting Started Guide Robot API.pdf`, **Required Reinforcement SDK**, exemplos). COM: `Exe\Interop.RobotOM.dll` |
| Integração Revit ↔ Robot | 2025 e 2026 | troca de modelo analítico | Autodesk | Extensão oficial (envia o modelo analítico do Revit ao Robot e volta) |
| SAP2000 | 21.1.0 | análise, dimensionamento (aço/concreto por normas estrangeiras) | `C:\Program Files\Computers and Structures\SAP2000 21` | **OAPI**: `CSiAPIv1.dll` + `CSI_OAPI_Documentation.chm` (.NET / Python via COM) |
| CYPE 3D | 2026.c | análise e dimensionamento de estruturas de barras | `C:\CYPE Ingenieros\Versão 2026\CYPE 3D` | IFC / Open BIM (API a verificar) |
| TQS | (pasta `C:\TQSW`) | análise, dimensionamento e detalhamento NBR — inclui **PREFAB** (pré-moldados) | `C:\TQSW\EXEC`, projetos em `C:\TQS` | **API Python oficial**: `C:\TQSW\EXEC\PYTHON\TQSPythonInterface-1.2.7-py310-none-any.whl`, manual `Programacao_TQS_Python.pdf`, exemplos `TST*.PY` (geometria, ferros, desenhos, edifício). Há `SUPORTE-PY\PREFAB` |
| AltoQi Eberick (+ Builder) | — | análise, dimensionamento e detalhamento NBR | `C:\Program Files\AltoQi\Eberick` | IFC (`AppSimplificadorIFC`), plugins (a verificar) |
| GstarCAD 2021 + Armadura BR | 0.1.3 (nosso) | detalhamento 2D | `C:\Program Files\Gstarsoft\GstarCAD2021` | .NET (plugin próprio do escritório) |
| ACCA usBIM.viewer+ | 9.0 | visualização IFC | `C:\ACCA\usBIM.viewer+` | — |

Projetos de exemplo do TQS em `C:\TQS` (inclui `MODPREO` — provável modelo pré-moldado — e `CPQ04 - Blocos`).

## Ferramentas de desenvolvimento
- .NET SDK 8.0.426, Visual Studio Community 2026, Build Tools 2026, VS Code, Git 2.56, Node.js 24.
- Python 3.14 (usuário) e 3.15 (`C:\Python315`); `numpy 2.5`, `scipy 1.18` no 3.14.
  - A API do TQS é um wheel `py310`: provavelmente exige um Python 3.10 à parte (verificar no manual).
  - Bibliotecas como OpenSeesPy podem não ter wheel para 3.14/3.15 ainda → usar `venv` com a versão suportada.

## Primeiras observações
- **Robot** é o caminho mais curto para análise a partir do Revit (extensão de integração + SDK com
  armadura necessária). Bom para comparar com o que fizermos.
- **TQS** é o único instalado com dimensionamento/detalhamento NBR **e** API Python aberta, incluindo pré-moldados.
- **SAP2000** tem a OAPI mais madura para automação de análise pura.
