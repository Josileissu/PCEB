# FreeCAD (módulos BIM, TechDraw e FEM)

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| Comunidade FreeCAD (freecad.org, github.com/FreeCAD/FreeCAD) | **LGPL-2.1** ✓ (`LICENSE`) | C++ ✓ (núcleo, Gui), **Python** ✓ (módulos e scripts) | não (código-fonte **27.1.0-dev** (branch `main`, `version.json`) em `programas/freecad/download`) |

Raio-X de 05/11/2026 pelo código-fonte (`docs/notas/ferramentas/raiox_fonte.py`), sem compilar nem rodar.

## Arquitetura e tecnologia ✓
- 13.313 arquivos: C/C++ ~1,7 milhão de linhas, Python ~810 mil; 1.183 arquivos de tradução Qt (27 em **pt-BR**).
- Interface **Qt** (PySide6 para Python); 3D com **Coin3D (Open Inventor) sobre OpenGL** e Pivy em Python.
- Geometria **Open CASCADE** (`Part`, STEP/IGES/BREP), Eigen, Boost, Clipper2; FEM com malha **SMESH/Gmsh/Netgen**,
  VTK e MED/HDF5.
- 30 módulos (bancadas) em `src/Mod`: `Part`, `PartDesign`, `Sketcher`, **`BIM`**, **`TechDraw`**, **`Fem`**,
  `Draft`, `Assembly`, `Spreadsheet`, `Mesh`, `CAM`, `Material`, `AddonManager`…
- Tudo scriptável em **Python** (console e macros); objetos paramétricos em Python com propriedades tipadas.

## BIM ✓
- 80 comandos (`bimcommands/`): parede, laje, pilar (`BimColumn`), viga (`BimBeam`), estrutura, treliça, pórtico,
  painel, telhado, escada, eixos, **corte** (`BimSectionPlane`), **tabela/quantitativo** (`BimSchedule`, `BimReport`),
  vistas em prancha (`BimTDPage`, `BimTDView`), camadas, classificação.
- **IFC nativo** (`nativeifc/`, 20 módulos) sobre o **IfcOpenShell**: o documento pode ser o próprio IFC
  (importa, edita, grava, psets, materiais, diferenças).

## Armadura ✓ — o que tem no código principal
- **`ArchRebar.py`** (743 linhas): barra paramétrica = um **esboço** (forma da barra) varrido pelo diâmetro, preso a
  uma peça **hospedeira** (`Host`) e repetido ao longo dela. Propriedades: `Diameter`, `OffsetStart`/`OffsetEnd`,
  `Amount`, `Spacing`, `CustomSpacing` (espaçamentos variáveis em texto), `Direction`, `Rounding` (raio de dobra em
  múltiplos do diâmetro), `Mark` (posição), `RebarShape` (ex.: `Stirrup`), `Length` e `TotalLength` (calculados,
  só leitura). Exporta como `IfcReinforcingBar`.
- O **FEM** tem material "concreto armado" (`MaterialReinforced`: matriz + armadura distribuída) para o CalculiX;
  e um material **B500** (FIB) na biblioteca.
- **Não estão no código principal**: as formas prontas (reto, U, L, estribo, hélice), as armaduras de pilar, viga,
  laje e sapata, a **tabela de dobramento (BBS)** e o desenho cotado das barras. Esses estão no **complemento
  "Reinforcement"** (`RebarTools`), que o `BIM/InitGui.py` carrega se estiver instalado ✓ — **não baixado**.

## TechDraw (pranchas) ✓
- Vistas projetadas de qualquer sólido, grupos de projeção, **cortes** simples e compostos, **detalhes ampliados**,
  vista quebrada, hachuras, **cotas** (inclusive automáticas), linhas de chamada, balões, anotações ricas, símbolos
  de solda, vista do BIM (`DrawViewArch`).
- 136 **folhas-modelo SVG** (ISO 5457 A0–A4, ANSI, Arch), com campos editáveis do carimbo; exporta **PDF, SVG, DXF**.
- Prancha é atualizada quando o modelo muda (paramétrico).

## FEM ✓
- Solvers: **CalculiX**, Elmer, FEniCS, Mystran, Z88; malha Gmsh/Netgen; pós com VTK.

## Avaliação
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 4 | maduro (o download é a versão em desenvolvimento); BIM e TechDraw em evolução |
| Manutenção | 5 | muito ativo |
| Documentação | 3 | wiki boa para usuário; código Python legível |
| Integração | 5 | Python em tudo; LGPL; IFC nativo |
| Aderência | 3 | modelo + armadura + prancha no mesmo programa, mas sem regras NBR nem padrão de pré-moldado |
| Licença | 5 | LGPL-2.1 (pode ser usado como biblioteca; compatível com o nosso GPL-3) |

## O que aproveitar para o nosso programa
1. **`ArchRebar` é quase a nossa camada 1 (intenção)**: forma + hospedeiro + quantidade/espaçamento + raio de dobra
   + posição; comprimentos calculados e só leitura (nossa camada 2). Confere com a diretriz de três camadas do
   `motor_detalhamento.md`. Não tem a camada 3 (sobrescrita marcada).
2. **`CustomSpacing` em texto** (ex.: "5@100+10@150") — formato simples para faixas de estribos que a IA também consegue escrever.
3. **TechDraw**: referência de prancha gerada pelo modelo (projeção, cortes, detalhes, cotas automáticas, carimbo SVG
   com campos) — o mesmo que fazemos no Revit, mas aberto.
4. **Rota sem Revit**: motor D19 → IFC (IfcOpenShell) → FreeCAD BIM/TechDraw para revisão ou pranchas, ou para
   escritórios sem licença Autodesk (requisito web/Windows do PCEB).
5. **Traduções pt-BR já existem** — interface em português sem trabalho extra se usarmos o FreeCAD como visualizador.

## A confirmar
- Baixar e fazer o raio-X do **complemento Reinforcement** (formas prontas, armaduras de pilar/viga/laje/sapata,
  BBS, desenho de barras) — é a parte que mais interessa ao D19.
- Testar se o `IfcReinforcingBar` gerado pelo FreeCAD abre certo no Revit e no Tekla (PC2).
