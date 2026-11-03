# Interface Revit ↔ análise

Objetivo: a partir do modelo no Revit, obter esforços, dimensionar (NBR 6118 / 9062) e alimentar o
detalhamento que o StructuralDetailer já gera.

## Caminhos possíveis (a estudar)
| Opção | Como | Prós | Contras |
|---|---|---|---|
| A. Robot via integração oficial | Modelo analítico do Revit → Robot → resultados | Já instalado; SDK com armadura necessária | Dependência da Autodesk; dimensionamento NBR limitado |
| B. Robot/SAP2000 via API | Nosso plugin monta o modelo pela API (RobotOM / CSiAPIv1) | Controle total do modelo e das combinações | Licença dos programas |
| C. Motor próprio/aberto | Exportar modelo analítico (JSON) → PyNite/OpenSees → JSON de esforços | Sem licença; adaptável a pré-moldado (fases) | Nós validamos tudo |
| D. TQS / Eberick | IFC ou API Python do TQS | Dimensionamento NBR pronto | Ida e volta com o Revit é indireta |

Ideia comum a todas: um **formato intermediário neutro** (JSON) de modelo analítico e de resultados,
para trocar o motor sem mexer no Revit.

## Base no StructuralDetailer
Comandos e AutoRun já existentes servem de molde (ver `src/DEB.Revit/src`):
levantamento (`PrecastSurvey`) → spec de armadura → layout → Revit.
