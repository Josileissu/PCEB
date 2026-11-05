# Matriz de funcionalidades (resumo das fichas)

● forte · ○ parcial · — não faz · ? a confirmar. Detalhes e fontes em cada ficha.

## Comerciais
| Função | TQS | Eberick | CYPE 3D | SAP2000 | ETABS | Robot | Revit | Tekla | IDEA | CAD |
|---|---|---|---|---|---|---|---|---|---|---|
| Instalado aqui | ✓ | ✓ | ✓ | ✓ | — | ✓ | ✓ | — | — | GstarCAD ✓ |
| Modelagem 3D | ● | ● | ● | ● | ● | ● | ● | ● | ○ | ○ |
| Análise global | ● | ● | ● | ● | ● | ● | — | ○ | — | — |
| Análise local (regiões D) | ○ | ○ | — | ○ | — | ○ | — | — | ● | — |
| Fases construtivas | ● | ? | — | ● | ● | ● | — | — | ● | — |
| Dimensionamento NBR | ● | ● | ● | ? | ? | ? | — | — | ? | — |
| Detalhamento de armaduras | ● | ● | ○ | — | — | ○ | ● | ● | — | ● |
| Pré-moldados | ● | ? | — | ○ | — | — | ○ | ● | ● | ○ |
| Fabricação / CAM | ? | — | ? | — | — | — | ○ | ● | — | — |
| Pranchas | ● | ● | ● | — | — | ○ | ● | ● | — | ● |
| IFC | ● | ● | ● | ○ | ○ | ● | ● | ● | ○ | — |
| DXF/DWG | ● | ● | ● | ○ | ○ | ○ | ● | ● | — | ● |
| Formato texto aberto | DAT | — | — | `$2k/s2k` | `e2k` | `str` | — | — | IOM | DXF |
| API | ● Python ✓ | — | — | ● OAPI ✓ | ● OAPI | ● COM ✓ | ● .NET ✓ | ● .NET | ● | ● .NET |

## Open source
| Função | OpenSees | CalculiX | PrePoMax | Gmsh | sectionproperties / concreteproperties | Blender + IfcOpenShell |
|---|---|---|---|---|---|---|
| Papel | motor de pórticos não linear | motor MEF sólidos | interface do CalculiX | malhas | seções e concreto armado | 3D / IFC |
| Licença | acadêmica ⚠ | GPL-2 | GPL-3 | GPL | MIT | GPL / LGPL |
| Linguagem de uso | Python/Tcl | arquivo `.inp` | C# (GUI) | Python | Python | Python |
| Pórtico / 2ª ordem | ● | ○ | ○ | — | — | — |
| Regiões locais (consolo) | — | ● | ● | apoio | — | — |
| Seções / flexão composta | ○ (fibras) | — | — | — | ● | — |
| Dimensionamento NBR | — | — | — | — | ○ (parâmetros manuais) | — |
| IFC | — | — | — | — | — | ● |
| Armadura (criar/detalhar) | — | — | — | — | — | — (só lê/grava `IfcReinforcingBar`) |
| IA / MCP | — | — | — | — | — | ● `ifcmcp` (IfcOpenShell) |

## Open source — 2ª leva (04/11/2026)
| Função | code_aster | XC (xcfem) | OpenCourant |
|---|---|---|---|
| Papel | motor MEF completo (EDF) | programa de cálculo por script (deriva do OpenSees) | dinâmica explícita (impacto, explosão) |
| Licença | GPL-3 | GPL-3 + aviso OpenSees ⚠ | AGPL-3 |
| Linguagem de uso | `.comm` (Python) | Python | `.rad` / `.k` |
| Pórtico / 2ª ordem | ● | ● | — |
| Regiões locais (consolo) | ● concreto não linear + protensão | ○ | — |
| Fases construtivas | ● | ● | — |
| Ações e combinações | ○ | ● | — |
| Dimensionamento por norma | ? (EC2, pós de armadura) | ● EC2/EC3/EC5/ACI/AISC… (sem NBR) | — |
| Windows | ? (container/WSL) | ? (Docker) | ● |
| Armadura | ○ áreas (`CALC_FERRAILLAGE`) | ○ seções de fibras, esquema TikZ | — |

Raio-X do código-fonte de todos os open source: `interfaces_e_desempenho.md`, seção 1.2 (05/11/2026). IfcOpenShell e
Bonsai têm ficha própria (`opensource/ifcopenshell.md`).

## Outras ferramentas (fichas em `outros/`)
- **RH Engenharia – Bloco de Estacas 3D + Memorial** (página HTML, licença desconhecida): reações em estacas por bloco rígido,
  grelha, as-built, memorial passo a passo, IFC/DXF/PDF. Referência para fundações e para o formato do memorial.

## Nossos (fichas em `proprios/`)
| Função | Armadura BR (CAD) | StructuralDetailer (Revit) |
|---|---|---|
| Plataforma | GstarCAD 2021, AutoCAD 2021–2027 (ZWCAD previsto) | Revit 2025/2026 |
| Versão / situação | 0.1.23, em uso | em desenvolvimento |
| Modelagem 3D | — (desenho 2D) | ○ (lê o modelo Revit) |
| Análise / dimensionamento | — (por decisão) | — (só verificações construtivas) |
| Detalhamento de armaduras | ● barras, estribos, faixas, telas, treliças, cordoalhas | ● Rebar por grupo de peças |
| Templates paramétricos | ● base de reservatório; pilar PM em construção | ● pilar PM |
| Pré-moldados | ○ pilar (P56/P61), catálogos de consolos e insertos | ○ pilar (PP127) |
| Relação do aço / quantitativos | ● | ● |
| Pranchas | ○ detalhamento (a folha é do projetista) | ● prancha completa |
| Importa | `.cad` QiArmaduras, reconhecimento de texto | — |
| Núcleo sem plataforma | ● `Armadura.Core` (netstandard2.0) | ○ `ColumnRebarLayout` |
| Testes automáticos | ● xUnit + autoteste no CAD | ● AutoRun |

## A catalogar (ampliação do escopo, 03/11/2026 — nomes a confirmar)
- Aço NBR 8800 / 14762: **mCalc** (Stabile), **Dimperfil** (CBCA, formados a frio), CYPE 3D (já catalogado — conferir normas),
  IDEA Connection (já na ficha IDEA), Tekla (aço).
- Vento NBR 6123: **Visual Ventos** (gratuito), módulos de vento do TQS/Eberick/CYPE.
- Steel frame / wood frame: programas de painelização e fabricação (ex.: FrameCAD, Vertex BD) — levantar.
- CFD: **feito** — OpenFOAM, AeroSim e similares em `cfd_vento_similares.md`.
- Em todas as fichas: acrescentar uma linha "materiais/sistemas cobertos" (concreto in loco, pré-moldado, aço laminado,
  formado a frio, steel frame, madeira).

## Leitura rápida
- **Ninguém cobre tudo** para pré-moldado + NBR + fabricação. TQS chega mais perto no Brasil; Tekla na fabricação; IDEA nas regiões D.
- Entre os abertos, **sectionproperties/concreteproperties** é o que mais encaixa no dimensionamento
  (licença MIT); **CalculiX/PrePoMax/Gmsh** formam um conjunto para análise local; **OpenSees** para não linearidade, com ressalva de licença.
- **code_aster** é o mais completo dos abertos para análise local com concreto fissurado e protensão; **XC** é o mais
  parecido com um programa de cálculo completo (ações, combinações, verificações por norma), mas herda a restrição do OpenSees.
- Os **nossos** já cobrem detalhamento e documentação em CAD e Revit; falta a parte de análise e dimensionamento.
  O pilar pré-moldado existe nos dois plugins, com padrões diferentes (P56/P61 × PP127).
