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
