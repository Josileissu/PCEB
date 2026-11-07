# Pendências do PC2 para o PC1 consolidar

O PC2 não edita `escopo_organizado.md` nem `matriz_funcionalidades.md`. Decisões, perguntas e linhas de matriz
encontradas lá entram aqui; o PC1 passa para os arquivos certos no próximo merge e marca como consolidado.

| Data | Assunto | Texto proposto / pergunta | Consolidado? |
|---|---|---|---|
| 05/11/2026 | DEB.Cad: pedidos feitos no PC2 (direto na `main`, f4dfa56) | Registrar no escopo: (1) cópia de barra mantém posição e rotação do texto do original; (2) MATCHPROP copia propriedades do plugin barra→barra, faixa→faixa e tabela RA→tabela RA, sem mudar geometria; (3) faixas em cota ortogonal e alinhada nascem sem o diâmetro | não |
| 05/11/2026 | Inventário | `inventario_pc2.md`: o PC2 tem Tekla Structures 2025 (ambiente Brazil), Tekla Structural Designer 2025, Tedds, IDEA StatiCa 26, Advance Steel 2026 e mais módulos do CYPE (Consoles curtos, StruBIM). Atualizar `matriz_funcionalidades.md` se fizer sentido | não |
| 05/11/2026 | DEB.Cad: template de reservatório com reconhecimento (pedido do usuário no PC2) | Pedido: reconhecer a estrutura de um DWG de outro programa (ex.: Eberick: 4 paredes externas, 1 interna, laje de fundo, lajes) e gerar o detalhamento no padrão de `src/DEB.Cad/auxiliar_cad/reservatório` usando só as bitolas e espaçamentos do desenho original. Decisões do usuário: (1) reconhecer DIRETO, sem janela de confirmação; (2) ganchos, ancoragens e traspasses pela NBR 6118, com os valores do exemplo como padrão do escritório editável; (3) v1 com tudo: paredes, fundo, laje intermediária e tampas; (4) código feito no PC2, branch `pc2/template-reservatorio` (D19: motor de superfícies, reservatório = paredes + fundo + tampa) | não |
| 05/11/2026 | Reservatório: ordem de trabalho (decisão do usuário) | Pausar o template de reservatório (v1 na branch `pc2/template-reservatorio`). Fazer primeiro o template de LAJES, depois o de PAREDES, no esquema do pilar e da base de reservatório, trabalhando os esquemas de armação de cada elemento e o detalhe dos cantos e encontros; depois juntar no template de reservatório. Coincide com a ordem da D19 (lajes maciças → paredes → reservatórios) | não |
| 06/11/2026 | DEB.Cad: template de viga pré-moldada (pedido do usuário no PC2) | Pedido: levantar todos os requisitos do detalhamento de viga pré-moldada e depois trabalhar nas informações para elaborar o detalhamento; o usuário vai enviar o padrão a seguir. Levantamento em `src/DEB.Cad/docs/VIGA_PREMOLDADA_REQUISITOS.md` (branch `pc2/viga-premoldada`). A viga entrou antes das lajes por pedido do usuário | não |
| 07/11/2026 | Raio-X das instalações do PC2 (P2-2) | Juntar as linhas da seção abaixo na tabela 1.1 de `interfaces_e_desempenho.md` (título passa a "Raio-X das instalações do PC1 e do PC2") e as leituras novas nas "Leituras para o PCEB" | não |

## Raio-X das instalações do PC2 (07/11/2026)
`python -I docs/notas/ferramentas/raiox.py "<nome>" "<pasta>"`, só leitura, mais conferência manual dos achados. Falsos
positivos descartados: `libifcoremd.dll` (é o runtime do Intel Fortran, não IFC), bitmaps `NSPA_*` do Tekla (não são ACIS),
arquivos `.xtp` do AutoCAD (são paletas de ferramentas, não Codejock), `DevExpress.Xpf.NavBar` (não é VBA), arquivos `V8-*.dgn`
(MicroStation V8, não JavaScript), `OSGeo.FDO` (não é OpenSceneGraph), `ResultsTextPresentation` do IDEA (não é Codejock).

| Programa | Tamanho / DLLs | Interface | Gráfico | Geometria / DWG / IFC | Cálculo | Script / dados / licença |
|---|---|---|---|---|---|---|
| **Tekla Structures 2025** (`C:\Program Files\Tekla Structures\2025.0`) | 3,9 GB; 1.909 (1.198 .NET) | **.NET** + **DevExpress 21.2** + WebView2 | **OpenGL** (`glew32`, driver `WinOpenGL` da ODA) + Direct3D 12 | **ODA/Teigha 21.11** (DWG/DGN, `TD_AcisBuilder` para sólidos ACIS), **Open CASCADE 7.8** (`TK*-780`), exportador IFC próprio (`IFCExport`, `IFCExport4`) | Intel TBB, OpenMP (`libiomp5md`) | **Open API .NET** (70 DLLs `Tekla.Structures.*`: Model, Drawing, Analysis, Catalogs, Dialog, CustomPropertyPlugin, DirectManipulation); **macros em C#** compiladas com Roslyn (`Microsoft.CodeAnalysis.CSharp`, `Tekla.Macros.*`); gRPC; SQLite (31); FlexNet |
| **Tekla Structural Designer 2025** | 0,4 GB; 314 (290 .NET) | .NET + DevExpress 21.2 + WebView2 | **HOOPS** (Tech Soft 3D: `Hoops.dll`, `Hoops Vision`) | IFC próprio da Trimble (`Tekla.Technology.IfcLib`) | solver CSC próprio (`cm2lapack`, LAPACK), Intel MKL/OpenMP | **API gRPC** (`TSDAPI.dll`, já no inventário); SQLite |
| **Tekla Tedds 27** | 0,04 GB; 142 (114 .NET) | .NET + WebView2 (+ MFC) | — | — | (cálculo nas folhas Tedds) | suplemento do Word (`.vsto`), bibliotecas COM (`.tlb`); SQLite |
| **IDEA StatiCa 26.0** | 1,9 GB; 3.608 (**3.501 .NET**, só 107 nativos) | **.NET (WPF)** + **Telerik** (68) + Syncfusion (documentos) + **CefSharp/Chromium** e WebView2 | **SharpDX** (Direct3D 10/11, Direct2D) | IFC por **GeometryGym** (`GeometryGymIFCcore`) | MEF próprio (`IdeaRS.SolverController`, `AnalysisModelMesher`) com **Intel Fortran** + LAPACK (`cm2lapack`) + MKL/OpenMP; módulo de protensão (`CI.Prestress*`) | **API .NET** (`IdeaStatiCa.Api`, `BimApi`, `BimApiLink`), **REST** (`ConnectionRestApi.exe`), gRPC; **links prontos** para Revit 2021–2026, Tekla 2021–2026, SAP2000 (`CSiAPIv1`), ETABS, RFEM, RSTAB, SCIA, STAAD, Midas, Advance Design; SQLite/Entity Framework |
| **AutoCAD 2026** (pasta com os toolsets: Architecture `ACA`, Civil 3D `C3D`, Map, Mechanical, Electrical, **Advance Steel `ADVS`**) | 6,8 GB no total (C3D 1,9; ADVS 0,7; Map 0,6; ACA 0,4); 3.821 (1.748 .NET) | nativo (MFC) + **BCGSoft** + **Qt6** + WebView2 | **Autodesk OGS** (Direct3D 9/10/11, pastas próprias); Helix Toolkit/SharpDX nos toolsets | **RealDWG/ObjectARX** (`acdb25`, `acge25`), ACIS (`acismobj25`); ODA 24.12 só nos toolsets (Civil/Map); IFC (`AecIfc*`, Architecture) | Intel TBB | **.NET** (`acmgd`, `acdbmgd`, `accoremgd`; o DEB.Cad usa estes), AutoLISP, VBA (opcional), Dynamo nos toolsets (1.454 `.dyn`); **Advance Steel**: ligações em .NET (`ASNetJoints`, `ASMgdJoints`, `AsConnectionsFramework`); SQLite; FlexNet |

**Leituras novas para o PCEB**
- **Tekla** usa **Open CASCADE** (o mesmo núcleo do FreeCAD) junto com a ODA: um comercial de detalhamento de ponta com
  geometria aberta reforça a escolha de OCCT para o núcleo próprio.
- **IDEA StatiCa** é quase todo .NET (97% das DLLs), inclusive o solver controlado por .NET com núcleo em Fortran: mostra
  que um programa de cálculo moderno pode ter interface e orquestração em .NET e só o núcleo numérico nativo.
- **IDEA** tem API REST e links com Revit e Tekla: dá para mandar consolos e dentes Gerber do DEB.Revit para o IDEA
  (módulo Detail) sem escrever o MEF, quando chegar a etapa de verificação.
- **Tekla** compila macros em C# com Roslyn dentro do programa: modelo para os "scripts do usuário" do PCEB.
- **Gráfico**: OpenGL (Tekla), HOOPS (TSD), SharpDX/Direct3D (IDEA), OGS (Autodesk); nenhum usa Vulkan de fato.
