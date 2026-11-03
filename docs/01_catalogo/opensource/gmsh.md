# Gmsh

| Origem | Licença | Linguagem | Instalado aqui |
|---|---|---|---|
| C. Geuzaine e J.-F. Remacle (gmsh.info) | **GPL-2 ou posterior** (com exceções para ligação — confirmar) | C++; API em C, C++, **Python**, Julia | não |

## Funções
- **Geometria**: kernel próprio e **OpenCASCADE** (booleanas, STEP/IGES/BREP).
- **Malha**: 1D, 2D, 3D (tetra, hexa, prismas), malha estruturada, refinamento por campos, malha de alta ordem.
- **Pós-processamento**: visualização de campos escalares/vetoriais.
- **Formatos**: `.geo` (script), `.msh`, exporta para `.inp` (Abaqus/CalculiX), `.unv`, VTK, STL e outros.
- **Análise / dimensionamento**: não — é gerador de malha.

## Avaliação (provisória)
| Critério | Nota | Comentário |
|---|---|---|
| Maturidade | 5 | padrão de fato em malhas abertas |
| Manutenção | 5 | ativo |
| Documentação | 4 | manual e muitos exemplos |
| Integração | 5 | API Python; script `.geo`; executável |
| Aderência | 3 | ferramenta de apoio para MEF (consolos, sólidos) |
| Licença | 3 | GPL: usar como programa/processo externo |

## Uso possível para nós
- Gerar malhas dos consolos/peças para o CalculiX, por script, a partir da geometria do Revit.
