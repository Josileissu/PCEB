# Blender

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| Blender Foundation (blender.org) | **GPL** ✓ (`COPYING`); bibliotecas internas Apache-2, MIT, BSD ✓ | C/C++ ✓; **API Python** ✓ | não (código-fonte **5.4 alpha**, branch `main`, em `programas/blender/download`) |

Bonsai e IfcOpenShell têm ficha própria: `ifcopenshell.md`. Raio-X de 05/11/2026 pelo código-fonte
(`docs/notas/ferramentas/raiox_fonte.py`), sem compilar nem rodar.

## Arquitetura e tecnologia ✓
- 20.945 arquivos: C/C++ ~3,9 milhões de linhas, Python ~530 mil, shaders GLSL 341 arquivos.
- **Interface própria** (GHOST + UI desenhada pela GPU), sem Qt nem toolkit nativo; toda a interface é scriptável em Python.
- **Motor gráfico** com backends **OpenGL**, **Vulkan** e **Metal** (`source/blender/gpu/`) ✓; render **Cycles**
  (CUDA, HIP, oneAPI, Metal, Embree) e EEVEE.
- Bibliotecas: TBB (paralelismo), Eigen, Manifold (booleanas), OpenSubdiv, OpenVDB, USD, MaterialX, OpenImageIO/EXR,
  Bullet (física), Mantaflow (fluidos).
- **Python embutido** com numpy; extensões em `scripts/addons_core` e sistema de pacotes (`bl_pkg`).

## Importa / exporta ✓
- Nativo (C++): **USD**, Alembic, FBX (ufbx), **OBJ**, **STL**, PLY, CSV, Grease Pencil (SVG/PDF).
- Complementos: **glTF 2.0**, FBX (exportação), SVG (curvas).
- IFC só com o Bonsai.

## Análise / dimensionamento
- Não. A física (Bullet, Mantaflow) é para animação e não serve para estruturas.

## Avaliação
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 5 | 30 anos, muito usado |
| Manutenção | 5 | muito ativo |
| Documentação | 4 | API Python documentada (`doc/python_api`) |
| Integração | 4 | Python; mas o programa é monolítico (não é biblioteca) |
| Aderência | 2 | visualização e render; não é ferramenta de engenharia |
| Licença | 4 | GPL, compatível com o nosso GPL-3 |

## O que aproveitar para o nosso programa
1. **Referência de motor gráfico multi-backend** (OpenGL/Vulkan/Metal atrás de uma camada `gpu`) — se o PCEB tiver
   visualizador próprio (web ou Windows), a mesma separação vale (ver `interfaces_e_desempenho.md`).
2. **Interface 100% scriptável em Python**: modelo para a IA e o usuário automatizarem tudo.
3. **Render de peças e da obra** (Cycles) para apresentação, pelo glTF/USD que exportarmos.
4. Base do **Bonsai** (BIM IFC), que é o uso mais útil do Blender para nós.

## A confirmar
- Se vale instalar o Blender + Bonsai para testar desenho de prancha e IFC de armadura (só com autorização).
