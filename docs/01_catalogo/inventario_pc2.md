# Inventário do PC2 (PC-CASA, levantado em 05/11/2026)

Levantado pelo registro de programas instalados e pelas pastas de instalação. Itens ✓ foram conferidos na pasta;
a API foi identificada pelas DLLs/arquivos, sem abrir os programas.

## "Tekla" × "Tekla Structures"
O usuário citou os dois. No PC2 há **três produtos Tekla diferentes**:
- **Tekla Structures 2025 SP6** (225.6) + ambiente **Brazil**: modelagem BIM e **detalhamento** (pré-moldados,
  armaduras, pranchas, numeração). É o que interessa à D19 e à ficha `comerciais/tekla.md`.
- **Tekla Structural Designer 2025** (25.0.1): análise e dimensionamento (não detalha pranchas).
- **Tekla Tedds** 27.0.1: memórias de cálculo (planilhas de cálculo de engenharia).

## Programas estruturais instalados

| Programa | Versão | Etapas | Caminho | API / automação |
|---|---|---|---|---|
| Tekla Structures ✓ | 2025 SP6, ambiente Brazil (`C:\ProgramData\Trimble\Tekla Structures\2025.0\environments\Brazil`) | modelo, **detalhamento**, pranchas | `C:\Program Files\Tekla Structures\2025.0` | **Open API .NET**: `bin\Tekla.Structures.Model.dll`, `.Drawing.dll`, `.Plugins.dll`, `.Analysis.dll`, `.Catalogs.dll` |
| Tekla Structural Designer ✓ | 2025 (25.0.1.45) | análise, dimensionamento | `C:\Program Files\Tekla\Structural\Structural Designer 2025` | `TSDAPI.dll` (gRPC) |
| Tekla Tedds | 27.0.1 | memórias de cálculo | `C:\Program Files\Tekla\Structural\Tedds` | a verificar |
| IDEA StatiCa ✓ | 25.1.2 e **26.0.0** | ligações, **Detail** (regiões D: consolos, dentes), RCS (seções de concreto), Member, Beam, Checkbot | `C:\Program Files\IDEA StatiCa\StatiCa 26.0` | `IdeaStatiCa.Api.dll`, `BimApi`/`BimApiLink`, **REST**: `ConnectionRestApi.exe`, `RcsRestApi.exe`; IOM (`IOM_IDEABeamRunner.exe`); links para SAP2000, ETABS, RFEM, SCIA, STAAD |
| Autodesk Revit ✓ | 2021, 2023, 2024, 2025 (25.0.2), 2026 (26.0.4) | modelo, detalhamento | `C:\Program Files\Autodesk\Revit 20xx` | API .NET; Add-In Manager 2025/2026 instalado; **DEB.Revit ainda não instalado** |
| AutoCAD 2026 ✓ (+ Architecture, Electrical, Mechanical, **Advance Steel**) | 25.1 | desenho 2D, detalhamento metálico | `C:\Program Files\Autodesk\AutoCAD 2026` | .NET; **DEB.Cad instalado** (bundle em `%AppData%\Autodesk\ApplicationPlugins\DEB.bundle`) |
| Robot Structural Analysis Professional ✓ | 2025 e 2026 (+ SDK 2025 e 2026, integração com Revit) | análise, armadura necessária | `C:\Program Files\Autodesk` | SDK/COM (igual ao PC1) |
| SAP2000 ✓ | 21 (21.0.2) e 14 | análise, dimensionamento | `C:\Program Files\Computers and Structures\SAP2000 21` | **OAPI**: `CSiAPIv1.dll` + `CSI_OAPI_Documentation.chm`; CSiXRevit 2025 |
| CYPE ✓ | 2026.a (e menu 2025.c): CYPECAD, CYPE 3D, CYPE Connect, **Consoles curtos**, Vigas contínuas, Elementos de fundação, StruBIM (Steel, Shear Walls, Deep Beams, Cantilever/Embedded Walls), Verificação de punção, Biblioteca de detalhes construtivos, Plugin Open BIM - Revit | análise, dimensionamento, detalhamento NBR | `C:\CYPE Ingenieros\Versão 2026` | Open BIM / IFC (API a verificar) |
| AltoQi Eberick (+ Builder) ✓ | pasta `AltoQi2024` | análise, dimensionamento, detalhamento NBR | `C:\Program Files\AltoQi\Eberick` | traz Python 3.13 embutido (plugins a verificar) |
| TQS ✓ | 22 (`TQS.EXE` 22.0) | análise, dimensionamento, detalhamento NBR, **PREFAB** | `C:\TQSW` | API Python (`EXEC\PYTHON\TQSPythonInterface-1.2.7-py310`), `SUPORTE-PY\PREFAB`; **plugins TQS para Revit** 2024 e 2025 (`C:\TQS-Plugins\RST2024`, `RST2025`) |
| Navisworks | Manage 2026, Freedom 2026 | compatibilização | `C:\Program Files\Autodesk\Navisworks Manage 2026` | API .NET |

**Não instalados no PC2:** GstarCAD, ZWCAD (o DEB.Cad só pode ser testado aqui no AutoCAD).

## Ferramentas de desenvolvimento
- Git 2.56; **GitHub CLI (`gh`) não instalado**.
- .NET SDK 8.0.319, 9.0.304 e 9.0.307 (`C:\Program Files\dotnet`, fora do PATH, como no PC1).
- Visual Studio Community 2022 (17.14), VS Code 1.143, Node.js 24.14.
- Python 3.13.17 (usuário).
- Google Drive montado em `S:` com `S:\Meu Drive\Normas` ✓.

## Diferenças em relação ao PC1 (`inventario_local.md`)
- Só no PC2: **Tekla Structures, Tekla Structural Designer, Tedds, IDEA StatiCa, Advance Steel**, Navisworks Manage, Revit 2021–2024.
- Só no PC1: GstarCAD 2021 e ACCA usBIM.viewer+.
- CYPE no PC2 tem mais módulos (Consoles curtos, StruBIM, Connect), úteis para a matriz de detalhamento (P2-5).
