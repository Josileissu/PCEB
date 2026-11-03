# Blender (+ Bonsai / IfcOpenShell)

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| Blender Foundation (blender.org); Bonsai, ex-BlenderBIM (bonsaibim.org) | Blender **GPL**; Bonsai GPL-3; IfcOpenShell **LGPL-3** | C/C++; **API Python** | não |

## Funções
- **Blender**: modelagem 3D geral, renderização, animação, scripts Python, importa/exporta OBJ, FBX, glTF, STL…
- **Bonsai**: transforma o Blender em ferramenta BIM **nativa IFC** — cria/edita IFC, desenhos 2D a partir do modelo,
  quantitativos, **modelo de análise estrutural IFC** (`IfcStructuralAnalysisModel`), cronograma.
- **IfcOpenShell** (biblioteca, independe do Blender): ler/escrever IFC em Python/C++, geometria, validação.
- **Análise / dimensionamento**: não.

## Avaliação (provisória)
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 5 / 3 | Blender maduro; Bonsai em evolução rápida |
| Manutenção | 5 | muito ativos |
| Documentação | 4 | boa (Blender), média (Bonsai) |
| Integração | 4 | Python; IfcOpenShell utilizável sem Blender |
| Aderência | 2 | visualização e IFC; não é ferramenta de pré-moldado |
| Licença | 4 | IfcOpenShell LGPL permite usar como biblioteca |

## Uso possível para nós
- **IfcOpenShell** para o requisito de exportar/importar **IFC** no programa web/Windows.
- Blender para visualização/renderização de peças, se necessário.
