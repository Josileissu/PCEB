# Interfaces gráficas e desempenho (HPC / paralelismo)

## 1. Interface gráfica de cada programa
✓ = indício encontrado nos arquivos instalados (DLLs). Não prova que a interface inteira usa só aquilo; programas
grandes misturam tecnologias. Sem ✓ = conhecimento geral, a confirmar.

| Programa | Interface (janelas) | Gráfico 3D | Observação |
|---|---|---|---|
| Revit | nativo Win32/MFC + WPF ✓ (+ Qt e BCG/Codejock ✓) | DirectX/OpenGL ✓ | plugins em WPF ou WinForms |
| AutoCAD / GstarCAD | nativo Win32/MFC | próprio (DirectX/OpenGL) | plugins em WinForms/WPF (paletas) |
| Robot | nativo Win32 | DirectX ✓ | interface antiga |
| SAP2000 21 / ETABS | .NET (WPF ✓) | DirectX/OpenGL ✓ | CSI migrou para .NET |
| TQS | nativo Win32 (BCG/Codejock ✓) | DirectX/OpenGL ✓ | editor gráfico próprio |
| Eberick | **Qt** ✓ | DirectX/OpenGL ✓ | Qt permite multiplataforma |
| CYPE | nativo Windows | DirectX/OpenGL ✓ | |
| Tekla | .NET (WinForms/WPF) + núcleo C++ | DirectX | |
| IDEA StatiCa | .NET WPF | | também app web/nuvem |
| PrePoMax | .NET **WinForms** | **VTK** (via C#) | |
| CalculiX (cgx) | própria, simples | OpenGL/GLUT | |
| Gmsh | **FLTK** | OpenGL | |
| FreeCAD | **Qt** | Coin3D/OpenGL | |
| Blender | toolkit próprio | OpenGL/Vulkan | totalmente scriptável em Python |
| OpenSees, sectionproperties | sem interface (script); gráficos matplotlib | | |
| Armadura BR | paletas WinForms dentro do CAD | do CAD | |
| StructuralDetailer | WPF escrito em código (sem XAML) | do Revit | |

## 1.1 Raio-X das instalações do PC1 (05/11/2026) ✓
Levantado por varredura das pastas de instalação (nomes de arquivos e cabeçalho PE, só leitura; script
`docs/notas/ferramentas/raiox.py`: `python -I raiox.py "<nome>" "<pasta>"`). Falsos positivos descartados (ex.: `QT*.DWG` do TQS não é Qt; `V8-*.dgn` do Revit não é V8).

| Programa | Tamanho / DLLs | Interface | Gráfico | Geometria / DWG / IFC | Cálculo | Script / dados / licença |
|---|---|---|---|---|---|---|
| **TQS** (`C:\TQSW`, 998 arquivos em `EXEC`) | 1,6 GB; 956 (154 .NET) | nativo + **Codejock 13.4** e **BCGSoft** | Direct3D (D3DCompiler) + OpenGL via ODA | **ODA Teigha 19.12** (DWG nativo, ACIS builder); IFC próprio (`NE3D2IFC`, `NIFCImport`) | — (solver próprio, nativo) | SQLite; **API Python** (wheel); licença **HASP** |
| **SAP2000 21** | 1,4 GB; 155 (72 .NET) | **.NET/WPF** (`CSI.Core.UI`, `CSIRibbon`, `System.Xaml`) | **SharpDX → Direct3D 11/Direct2D** + `CSIOpenGL` (alternativo) | `IfcImportExport`, CIS/2 (aço), exportação para Revit (`SAP2000xRevit`) | **Fortran Intel** + **OpenMP** (`libiomp5`); solver separado (`CSI.SAPFire.Solver.exe`) | **OAPI** (`CSiAPIv1.dll`, `SAP2000v1.dll`); licença **Sentinel RMS** |
| **Robot 2026** | 1,1 GB; 1.850 (91 .NET) | **MFC** (interface antiga) | (motor gráfico próprio) | **RealDWG/ObjectARX** (`acdb25`), ACIS, **CGAL** | **Intel TBB** + OpenMP | **API COM** (`RobotOM`, SDK com .h/.cpp/.lib); BeSQLite |
| **Revit 2026** | 4,3 GB; 3.236 (2.209 .NET) | WPF + **WebView2** (28 DLLs) + Qt6 + BCGSoft + AvalonDock | **Autodesk OGS** (One Graphics System) sobre **Direct3D 11/12**; **USD/Hydra** (há backend Vulkan); Helix Toolkit (Dynamo) | **ODA 25.12** + RealDWG; IFC (exportador aberto da Autodesk) | Intel TBB | **API .NET**; **Dynamo** (1.418 nós `.dyn`); SQL LocalDB |
| **GstarCAD 2021** | 0,5 GB; 139 (3 .NET) | nativo (MFC) | próprio | **ODA** (`TD_*_3.05src`, versão própria), **ACIS** (`SpaACIS`), IFC 2x3 | — | ARX/GRX (`.grx`), .NET; licença **FlexNet** |
| **Eberick** (AltoQi) | 1,2 GB; 365 (58 .NET) | **Qt6** + **QtitanRibbon** + WebView2 | **OpenGL** (motor `Qi3D`, shaders) + **devDept Eyeshot** | **IfcPlusPlus** + **ifcengine** (RDF); DWG pela **Teigha**; BCF | solvers em processos separados; **Intel MKL**, **TBB**, **OpenBLAS/LAPACK**; Clipper, Carve, Poly2Tri | **Python 3.13 embutido = nó Python do Axis Nodes** (programação visual, pybind11); nuvem **AWS S3**; **mlpack**; SQLite, LocalDB |
| **AltoQi Builder** | 1,1 GB; 312 (53 .NET) | **Qt5** + WebView2 | OpenGL | IfcPlusPlus, ifcengine, `IFCParser` | TBB, OpenBLAS/LAPACK | SQLite (`qsqlite`) |
| **CYPE 3D 2026.c** | 1,0 GB; 677 (**0 .NET**) | nativo em C (toolkit e faixa de opções próprios) | **OpenGL + GLSL** próprio (`cyopengl`); exporta **glTF** e **FBX** | IFC/BCF/gbXML (Open BIM, BIMserver.center); DXF/DWG próprio | **OpenSees 3.2.2 embutido** (modal, pushover, sismo; OpenSeesSP/MPI); OpenMP; MEF próprio | sem API pública; **uma DLL por norma** (NBR 8800, 14762, 7190, 6123…); Git e PostgreSQL embutidos; exporta Tekla e Tecnometal |

**Leituras para o PCEB**
- **DWG**: todos os comerciais usam **ODA (Teigha)** ou **RealDWG**; ler/gravar DWG sem eles não é realista. Para nós: DXF aberto
  ou, se precisar de DWG, a ODA (associação paga; o ODA File Converter é gratuito para converter em lote).
- **IFC**: há bibliotecas abertas em uso comercial (**IfcPlusPlus** no Eberick/Builder; IfcOpenShell no ecossistema aberto).
- **3D**: Direct3D 11/12 nos programas Autodesk/CSI; **OpenGL** nos brasileiros (Eberick, TQS) e no CYPE. Exportar **glTF**
  (CYPE) é o caminho mais direto para visualizar no navegador (three.js) — encaixa na recomendação web da seção 2.
- **Cálculo**: paralelismo por **OpenMP** (SAP2000, CYPE) e **Intel TBB** (Robot, Revit, Eberick); álgebra com MKL/OpenBLAS.
- **Automação**: TQS (Python), SAP2000 (OAPI), Robot (COM), Revit (.NET + Dynamo), **Eberick (Axis Nodes:
  programação visual por nós + nó Python)**. CYPE sem API visível.
- **Tendência de interface**: **WebView2** já aparece no Revit (28 DLLs) e nos AltoQi — telas web dentro de programas nativos,
  exatamente o modelo proposto na seção 2.

## 1.2 Raio-X do código-fonte dos open source (05/11/2026) ✓
Lido direto dos pacotes em `programas/*/download`, sem extrair, compilar nem rodar (script
`docs/notas/ferramentas/raiox_fonte.py`: `python -I raiox_fonte.py "<nome>" <arquivo...>`). Linhas contadas por arquivo.

| Programa | Tamanho | Linguagens (linhas) | Interface | Gráfico | Geometria / formatos | Cálculo | Script / licença |
|---|---|---|---|---|---|---|---|
| **Blender** (5.4 alpha) | 20.945 arq. | C/C++ 3,9 mi; Python 530 mil; GLSL | própria (GHOST, desenhada pela GPU) | **OpenGL + Vulkan + Metal** (`gpu/`); Cycles (CUDA/HIP/oneAPI/Embree) | Manifold, Eigen; USD, Alembic, FBX, OBJ, STL, PLY, glTF | TBB; numpy | Python embutido; GPL |
| **IfcOpenShell** 0.9.0 (+ Bonsai) | 4.253 arq. | C/C++ 1,07 mi (esquemas IFC); Python 600 mil | Qt (ifcviewer); Blender (Bonsai); web (pyodide) | OpenGL | **Open CASCADE**, CGAL, Manifold; IFC2X3 → IFC4X3; SVG, **DXF (ezdxf)**, glTF, USD | — | Python/SWIG; **MCP (`ifcmcp`)**; LGPL-3 (lib), GPL-3 (Bonsai) |
| **code_aster** | 32.854 arq. | **Fortran 1,47 mi**; Python 720 mil; C/C++ 400 mil | — (Salome-Meca à parte) | — | malha MED | MPI, **MUMPS**, HDF5 | comandos em Python; GPL-3 |
| **XC** | 7.390 arq. | C++ 690 mil; Python 350 mil | — | VTK (pós) | CGAL, Boost, Eigen; Gmsh | MUMPS, UMFPACK, ARPACK, MPI, OpenMP | Python; normas EC/ACI/EHE/SIA; GPL-3 + aviso OpenSees |
| **OpenSees** | 6.347 arq. | C++ 1,68 mi; Fortran 130 mil; Tcl | — (Tk antigo) | OpenGL antigo | TetGen | UMFPACK, ARPACK, SuperLU, MUMPS, MPI, MKL | Tcl e Python; licença acadêmica ⚠ |
| **Gmsh** 4.15.2 | 4.589 arq. | C++ 1,38 mi | **FLTK** (+ Qt opcional) | OpenGL | **Open CASCADE** (STEP/IGES), MED, VTK | PETSc, MUMPS, Eigen | API C/C++/Python/Julia/Java; GPL-2+ com exceção |
| **PrePoMax** | 2.940 arq. | **C# 420 mil**; Python 50 mil | **WinForms** | **VTK** (`vtkControl`) | Open CASCADE; Gmsh, Netgen, mmg | (CalculiX) | API Python; GPL-3 |
| **CalculiX cgx** 2.23 | 426 arq. | C 190 mil | GLUT | OpenGL | TetGen, Netgen, NURBS (libSNL) | — | GPL-2 (**solver `ccx` não baixado**) |
| **OpenCourant** | 10.564 arq. | **Fortran 2,1 mi**; C/C++ 220 mil | — | — | — | MPI, MUMPS | AGPL-3 |
| **sectionproperties** | 168 arq. | Python 24 mil | — (matplotlib) | — | shapely; importa Rhino/DXF | numpy, scipy, numba | MIT |

**Leituras para o PCEB**
- **Nenhum open source cria ou detalha armadura**: IfcOpenShell só lê e escreve `IfcReinforcingBar`; XC e code_aster
  calculam áreas de aço. O motor D19 não tem atalho aberto (o FreeCAD Reinforcement, ainda não catalogado, é o único candidato).
- **IFC**: o IfcOpenShell é a biblioteca aberta de referência (LGPL, pode ser ligada); já traz um **servidor MCP** para IA.
- **Geometria**: **Open CASCADE** é o kernel aberto comum (IfcOpenShell, Gmsh, PrePoMax); CGAL/Manifold para booleanas.
- **Gráfico**: o Blender separa OpenGL/Vulkan/Metal atrás de uma camada própria; PrePoMax mostra **WinForms + VTK em C#**,
  o mais próximo da nossa pilha .NET.
- **Cálculo**: o núcleo pesado continua em Fortran/C++ (code_aster, OpenCourant, OpenSees) com MUMPS/UMFPACK — para a
  fase de análise, usar como motor externo e não reescrever.
- **Normas**: o XC organiza cada norma em um módulo Python (`materials/ec2`, `ehe`, `aci`…) — bom modelo para a NBR no núcleo.

## 2. Qual a melhor para o nosso projeto (recomendação, não decisão)
O requisito pede **web (local → nuvem)** e **Windows com as mesmas funções**. Fazer duas interfaces dobra o trabalho.
O caminho mais econômico é **uma interface web só**:

- **Web**: HTML/TypeScript; 3D com **three.js** ou **vtk.js** (o VTK é o mesmo do PrePoMax/ParaView); gráficos com bibliotecas JS.
- **Windows**: a mesma interface dentro de uma janela nativa (**WebView2**, ou Tauri/Electron), falando com o núcleo local.
- **Plugins** (Revit/CAD): continuam com WPF/WinForms para comandos curtos; telas grandes podem abrir a interface web
  dentro do plugin (WebView2), reaproveitando as mesmas telas.

Assim web, Windows e plugins compartilham **o mesmo núcleo** (cálculo, normas, catálogos) e quase a mesma interface.
Isso responde em parte a pergunta em aberto nº 3 do escopo — fica para você decidir.

Alternativas: Qt (como Eberick/FreeCAD) é ótimo para desktop, mas não roda no navegador; WPF/WinForms só Windows.

## 3. HPC, multi-thread e paralelismo
| Onde | Quanto importa para nós | Como os programas fazem |
|---|---|---|
| Solver de sistemas grandes (MEF de sólidos, edifícios grandes) | médio | solvers esparsos multi-thread (PARDISO, MUMPS, SPOOLES); SAP2000/Robot multi-thread; CalculiX com `OMP_NUM_THREADS`; OpenSees tem versões MPI (OpenSeesSP/MP) |
| **Muitas peças / muitas combinações** (lote de pilares e vigas, envoltórias) | **alto** | problema "trivialmente paralelo": cada peça/combinação num núcleo da CPU, ou em vários servidores na nuvem |
| Geração de pranchas no Revit/CAD | baixo | as APIs do Revit e do CAD são de **uma thread só**; o ganho vem de calcular fora e só desenhar dentro |
| CFD (vento por simulação) | muito alto | só é viável com cluster/nuvem (ver abaixo) |
| GPU | baixo por enquanto | útil em CFD e alguns solvers; não é prioridade |

Conclusão provisória: para pré-moldados o ganho real está em **processar em lote** (peças e combinações em paralelo) e,
na fase nuvem, em **fila de trabalhos** em servidores — não em supercomputação.
