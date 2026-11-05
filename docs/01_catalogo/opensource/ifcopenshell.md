# IfcOpenShell (+ Bonsai)

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| Comunidade IfcOpenShell (ifcopenshell.org, github.com/IfcOpenShell); Bonsai = ex-BlenderBIM | biblioteca **LGPL-3** ✓ (`COPYING.LESSER`); Bonsai e ferramentas **GPL-3** ✓; `opencdeserver` **AGPL-3** ✓ | C++ (núcleo) ✓, **Python** ✓ (SWIG) ✓, JS/TS e Rust (web) ✓ | não (código-fonte **0.9.0** em `programas/ifcOpenShell-bonsai/download`) |

Raio-X de 05/11/2026 pelo código-fonte (`docs/notas/ferramentas/raiox_fonte.py`), sem compilar nem rodar.

## Arquitetura e tecnologia ✓
- 4.253 arquivos: C/C++ ~1,07 milhão de linhas (77 MB, quase tudo esquemas IFC gerados em `ifcparse/schemas`),
  Python ~600 mil linhas (1.933 arquivos).
- **`ifcparse`**: leitura/escrita STEP de todos os esquemas — IFC2X3, IFC4, IFC4X1, IFC4X2, IFC4X3 (e revisões ADD1/ADD2/TC1).
- **`ifcgeom`**: geometria com **Open CASCADE**, **CGAL**, Eigen, Boost e Manifold; inclui `IfcSweptDiskSolid` (a
  geometria das barras de armadura) ✓.
- **`ifcopenshell-python`**: API de alto nível com 35 módulos (`api/root`, `geometry`, `material`, `profile`, `pset`,
  `type`, `spatial`, `structural`, `drawing`, `cost`, `sequence`, `classification`, `georeference`…).
- **Visualizadores**: `ifcviewer` (Qt + OpenGL), `ifcviewer-web` e `pyodide` (IfcOpenShell no navegador, em WebAssembly).
- **Ferramentas de linha de comando**: IfcConvert (IFC → OBJ, glTF, STEP, SVG, USD…), `ifcclash` (interferências),
  `ifcdiff`, `ifctester` (IDS — validação de requisitos), `ifccsv`, `ifcpatch` (receitas), `ifc4d`/`ifc5d`
  (cronograma e custo), `bcf`, `bsdd`.

## IA ✓ — destaque
- **`ifcmcp`** (LGPL-3): **servidor MCP** que carrega um IFC na memória e expõe consultas e edições (`ifc_new`,
  `ifc_load`, `ifcquery`, `ifcedit`). O README traz a configuração para o **Claude Code** (`claude mcp add … ifcmcp`).
- **`ifcchat`**: assistente web (pyodide) que conversa com o modelo IFC usando Anthropic/OpenAI/Gemini.
- É o mesmo padrão que escolhemos (IA orquestra ferramentas; os números vêm de código determinístico).

## Bonsai (BIM dentro do Blender) ✓
- 60+ módulos em `bonsai/bim/module/`: modelagem, `drawing`, `structural`, `qto` (quantitativos), `clash`, `cost`,
  `sequence`, `ifcgit` (versionamento do IFC no Git), `bcf`…
- **Desenhos e pranchas**: vistas, cortes e anotações em **SVG**; folhas-modelo A1/A2/A3 em SVG
  (`bim/data/templates/titleblocks`); exportação **DXF** com **ezdxf** ✓.
- **Estrutural**: cria `IfcStructuralAnalysisModel` (barras, apoios, cargas); **`ifc2ca`** gera a entrada do
  **code_aster** (`.comm`) a partir do IFC (pouco mantido: parte em `_deprecated`).

## Armadura — o que **não** tem ✓
- `IfcReinforcingBar`, `IfcReinforcingMesh`, `IfcTendon` e `BendingShapeCode` só aparecem nos **esquemas e regras**
  (`express/rules`), não em ferramentas: **não há módulo para criar ou detalhar armadura**, nem quadro de aço, nem
  `IfcPrecastConcreteElementType` usado no código.
- Ou seja: a biblioteca **lê e escreve** armadura IFC corretamente, mas a geração fica conosco.

## Avaliação
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 4 | núcleo IFC maduro; Bonsai em evolução rápida |
| Manutenção | 5 | muito ativo (0.9.0, IFC4X3, MCP) |
| Documentação | 4 | `docs/` para C++ e Python |
| Integração | 5 | Python, C++, WebAssembly e MCP |
| Aderência | 3 | IFC e desenhos sim; armadura e pré-moldado não |
| Licença | 4 | biblioteca LGPL-3 (pode ser ligada ao PCEB); Bonsai GPL-3 (compatível com o nosso GPL-3) |

## O que aproveitar para o nosso programa
1. **Saída IFC do motor D19**: o modelo de armadura neutro → `IfcReinforcingBar` (com `IfcSweptDiskSolid` e
   `BendingShapeCode`) e `IfcReinforcingMesh` pela `ifcopenshell.api`. Serve para trocar com Tekla/TQS/CYPE e para
   uma versão web ou sem Revit.
2. **`ifcmcp` como modelo** para o nosso servidor MCP (consulta + edição com o modelo em memória); dá para testar já
   com o Claude Code em um IFC exportado do Revit.
3. **`ifctester` (IDS)** para conferir se o IFC que exportamos tem os parâmetros que o escritório exige.
4. **Desenho em SVG + DXF (ezdxf)** do Bonsai como referência de pranchas geradas fora do CAD.
5. **pyodide**: prova de que o núcleo IFC roda no navegador (requisito web do PCEB).

## A confirmar
- Testar a criação de uma barra com `BendingShapeCode` e abrir o IFC no Revit e no Tekla (PC2).
- Testar o `ifcmcp` com o Claude Code (pede `pip install ifcopenshell-mcp`; só com autorização).
