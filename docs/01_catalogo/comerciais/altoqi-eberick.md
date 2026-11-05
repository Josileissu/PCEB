# AltoQi Eberick (+ Builder)

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| AltoQi (Florianópolis) | comercial; aplicações **Essential, Professional, Premium, Enterprise, Infinity** + sistemas adicionais; **modelo assinatura** (recursos novos só para assinantes) ✓ | Windows, 64 bits | ✓ `C:\Program Files\AltoQi\Eberick` (1,2 GB) e `Builder`; ajuda com 2.692 páginas cobre até a **atualização 2026-04** (versão interna 19.310) ✓ |

Levantamento de 05/11/2026 pelos arquivos instalados e pela ajuda local (`Ajuda\Eberick`, sumário em `contents_data.js`);
o programa não foi aberto.

## Arquitetura e tecnologia ✓
- **Interface Qt6** com **QtitanRibbon** (faixa de opções), docking próprio, **WebView2**; traduções pt-BR, en-US, es-MX.
- **3D**: motor próprio `Qi3D*` sobre **OpenGL** (glew/freeglut, shaders) + **devDept Eyeshot** (controle CAD 3D .NET).
- **DWG**: **Teigha (ODA)**; **IFC**: **IfcPlusPlus** e **ifcengine** (RDF), simplificador de IFC, **BCF** (`BCFTools`).
- **Cálculo**: solvers em processos separados (`QiLinearSolver.exe`, `QiEigenSolver.exe`), **Intel MKL**, **Intel TBB**,
  **OpenBLAS/LAPACK** (com gfortran), Clipper, Carve, Poly2Tri (geometria 2D/3D).
- **Nuvem**: persistência de modelos na nuvem (`Qi3DModelCloudPersistence`, **AWS S3 SDK**, gSOAP), login por JWT.
- **Aprendizado de máquina**: **mlpack** (`mlpackClassifierAPI`) — uso não documentado na ajuda.
- **Relatórios**: visualização interna, HTML, **DOCX** e XLSX (Open XML SDK).
- Créditos de terceiros listados na ajuda ✓: AWS SDK, ApproxMVBB, Boost, Carve, Clipper, CrashRpt, Eyeshot, FreeGLUT,
  gSOAP, IFC, Intel MKL e TBB, jwt-cpp, libcurl, mlpack, OWLNext, Open XML SDK, OpenGL/GLEW, OpenSSL, Poly2Tri,
  **pybind11**, Qt, RapidXML, SQLite, SuperEasyJSON, Teigha, WebView2, zlib.

## Modelagem (Lançamento) ✓
Entrada gráfica em CAD integrado, por pavimentos e **croquis**, com arquitetura de fundo em DXF ou **modelo IFC vinculado**.
- **Fundações**: fundação, pilar de fundação (comum, genérico, metálico, **pré-moldado**, de núcleo rígido), estaca isolada.
- **Pilares**: comum, genérico, **metálico**, **pré-moldado**; prumadas; detalhamento contínuo no lance.
- **Núcleos rígidos** (pilares-parede, MEF por lâminas — só Infinity/assinatura).
- **Vigas**: comum, inclinada, de fundação, genérica, **metálica**, **pré-moldada**; contraflecha; furos.
- **Lajes**: maciça, nervurada, treliçada, **vigotas protendidas**, **alveolares** (cadastro por fabricante com tabela
  vão × sobrecarga), de fundação, rampas, maciços, furos; **cabos e faixas protendidas**.
- **Escadas** (lances, patamares), **paredes de contenção e reservatórios** (parede, laje de reservatório), **muros**,
  barras e nós genéricos, **paredes de alvenaria** (alvenaria estrutural).
- Modelo: vínculos, **ligação semirrígida em viga pré-moldada** (fator de restrição definido pelo usuário ou
  **"barra passante"**, com condições: 2º estágio de concretagem, pilar pré-moldado, viga perpendicular à face), cargas.

## Análise ✓
- Pórtico espacial com estabilidade global (γz, não linearidade geométrica), lajes por **grelha** (plastificação de
  apoios, redistribuição pela NBR 6118:2023 14.6.4.3) ou modelo integrado lajes+vigas+pilares; vento; **sismo**
  (espectro de resposta); temperatura e retração; análise dinâmica; **etapas construtivas** em pré-moldados
  ("Esforço (construção)").

## Dimensionamento (NBR 6118:2023) ✓
Janelas de dimensionamento por tipo: blocos, sapatas, pilares (com **uniformização de prumadas** e otimização de
seção), pilares metálicos, **pilares pré-moldados** (com **consolos**), vigas (otimização de seção), vigas metálicas,
**vigas pré-moldadas** (seções subarmadas, **ligações por barra passante**), paredes de alvenaria, lajes, escadas, muros,
paredes e reservatórios, radier, **faixas protendidas**, núcleos rígidos, **verificação em situação de incêndio**.
- **Fundações pré-moldadas**: consolos da fundação, **comprimento de embutimento no cálice**, **folgas do cálice**.

## Detalhamento e documentação ✓
- Detalhamento com seleção de armaduras ("Acrescentar/Retirar barra", "Gravar bitola"), **editor de armaduras**,
  elementos de desenho Armadura, Chamada, **Faixa**, **Treliça**, Indicador.
- Critérios de detalhamento por elemento (pilares e núcleos, **esperas**, vigas, lajes).
- **Pranchas** configuráveis; pré-moldados agrupados por pavimento, tipo ou elemento, formato **prancha ou caderno**,
  **relação de aço por elemento e/ou por prancha**; plantas e pranchas de vigotas pré-moldadas; planta de locação;
  níveis de desenho e **perfis de níveis**; quantitativos por elemento, prancha, pavimento ou projeto.

## API / automação ✓
- **AltoQi Axis Nodes** (sistema adicional): **programação visual por nós** dentro do Eberick (122 páginas de ajuda):
  nós de entrada, matemáticos, lógicos, **listas**, **leitura do modelo** (pavimentos, pilares, vigas, sapatas, blocos e
  estacas, barras do elemento, propriedades), **resultados** (esforços, axial/fletor máximo, cortante e fletor nodal,
  combinações, ações), **desenho CAD** (linha, poligonal, círculo, arco, retângulo, hachura, cota, texto, **armadura**
  com pontos, quantidade, bitola e espaçamento) e **nó Python** (Python 3.13 embutido, pacotes externos via `pip`,
  pybind11). Rotinas reutilizáveis; exportação de armaduras para o editor de armaduras.
- `JsonMacro`/`JsonMacroAPI`, `InterfaceInteractionAPI`, `QiSpreadSheetAPI`: APIs internas (uso externo não documentado).

## Pré-moldados ✓
Pilares (com consolos), vigas (semirrígidas, barra passante, etapas de construção), fundações (cálice, embutimento,
folgas), lajes alveolares e vigotas protendidas; pranchas próprias com RA por elemento. Não aparecem na ajuda:
**içamento, transporte, insertos, neoprene, painéis** — nesses pontos o TQS PREFAB vai mais longe.

## Importa / exporta ✓
DXF/DWG (importar arquitetura, exportar desenhos), **IFC** (vincular modelo externo, exportar modelo e malha 3D com
quantitativos), BCF, DOCX/HTML/XLSX, modelos de projeto `.mrj`, integração com o AltoQi Builder.

## O que aproveitar para o nosso programa
1. **Programação visual por nós com nó Python** — mesma ideia do Dynamo no Revit; confirma que engenheiros querem
   automatizar dentro do programa. Para o PCEB: um núcleo com API Python (ou .NET) bem definida permite tanto nós
   quanto IA chamando ferramentas.
2. **Nó "Armadura"** (pontos + quantidade + bitola + espaçamento) — interface mínima de criação de armadura, boa
   referência para as ferramentas que a IA vai chamar.
3. **Ligação semirrígida "barra passante"** com regras de validade explícitas — exemplo de regra de pré-moldado
   modelada como restrição verificável.
4. **Cálice: embutimento e folgas** como critérios de projeto — entra no motor de fundações (D19).
5. **Relação de aço por elemento ou por prancha, prancha ou caderno** — opção a prever no nosso padrão de apresentação.
6. Bibliotecas abertas usadas por um comercial brasileiro: **IfcPlusPlus, Clipper, Carve, Poly2Tri, pybind11, Qt**.

## A confirmar
- Aplicação e sistemas licenciados no escritório (Pré-moldados? Axis Nodes?).
- Uso do mlpack (reconhecimento de elementos? sugestão de seções?).
- O que exatamente o IFC exporta (modelo físico, analítico, armaduras?).
