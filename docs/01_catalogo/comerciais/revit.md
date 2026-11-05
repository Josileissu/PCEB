# Autodesk Revit

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| Autodesk | comercial (assinatura) | Windows | ✓ 2025 (25.0.2) e 2026 (26.0.4); também Revit LT 2025 |

Levantamento de 05/11/2026 pelos arquivos instalados, `RevitAPI.xml` (2026: **2.769 tipos, 9.162 métodos**), os
add-ins embutidos (`AddIns\`) e a API do **Revit Precast** (`Autodesk.Precast.API.xml`), mais a experiência do
DEB.Revit (ex-StructuralDetailer).

## Arquitetura e tecnologia ✓
- Núcleo nativo + **.NET 8** (2.209 assemblies .NET); interface WPF/MFC com **WebView2** e Qt6; 3D pelo **OGS**
  (Direct3D 11/12) e USD/Hydra; DWG pela ODA + RealDWG; **Dynamo** e Dynamo Player embutidos.
- Add-ins da própria Autodesk instalados: **Precast**, **Rebar**, **SteelConnections**, MemberForces,
  AnalyticalAutomationTools, Numbering, SectionProperties, IFC, Robot link, Revit/Robot Structure Extensions.

## Modelagem e análise
- BIM paramétrico: famílias (aninhadas, compartilhadas), tipos, parâmetros, **assemblies**, **Parts**.
- **Modelo analítico** separado do físico (`AnalyticalMember`, painéis, cargas, apoios); análise só fora (Robot, outros).

## Detalhamento — API de armadura ✓ (144 tipos em `DB.Structure`)
- **Rebar** por forma (`RebarShape`, definição por segmentos ou arco, restrições) ou **livre** (`RebarFreeForm`,
  servidor de atualização próprio `IRebarUpdateServer`); ganchos e terminações; **acopladores** (`RebarCoupler`).
- **Distribuição**: regras de layout (número fixo, espaçamento máximo/fixo), propagação, restrições a faces do
  hospedeiro (`RebarConstraintsManager`), alças de edição.
- **Emendas por regras** (`RebarSpliceRules`, `RebarSpliceUtils`: posição, deslocamento, comprimento) — 2025+.
- **Detalhes de dobramento** (`RebarBendingDetail`: segmentos cotados, ângulos, diâmetro do pino, campos
  personalizados) — resolve as "formas das barras cotadas" das pranchas.
- **Numeração** (`ReinforcementNumberingMethod`, partições), **arredondamento** (`RebarRoundingManager`),
  **instruções de trabalho**, **abreviações** e **anotação múltipla** (`MultiReferenceAnnotation` — uma etiqueta para
  várias barras), cobrimentos (`RebarCoverType`).
- **Armadura por área e por caminho**, **telas** (`FabricSheet`, telas dobradas), contêineres de barras.

## Documentação ✓
- Vistas de assembly (`AssemblyViewUtils`), folhas, viewports, cotas, etiquetas, tabelas (`ViewSchedule`) — o DEB.Revit
  já gera pranchas de pilares pré-moldados por assembly.

## Pré-moldados — Revit Precast (embutido no 2025 e no 2026) ✓
- **Produtos**: parede maciça, **parede dupla**, laje maciça, **pré-laje treliçada** (GirderSlab), **laje alveolar**
  (HollowcoreSlab).
- **Divisão** (Split) de paredes e lajes em peças com tipos de ligação (padrão, unilateral, de canto), assemblies e
  contraventamento de içamento.
- **Armação automática** por configuração: armadura por área em camadas (barras, telas, treliças), armadura de borda.
- **Pranchas de fabricação** (shop drawings) por template, com eventos para cada etapa (vistas, etiquetas, cotas,
  posicionamento).
- **Exportação CAM**: **Unitechnik 5.2 e 6.0**, **PXML 1.3**, **UXML 7.01** (barras, treliças, peças embutidas).
- **API própria** (96 tipos): `ReinforcementCreator`, `ShopDrawingCreator`, `CamExporter`, eventos de split.
- Não cobre **pilares e vigas pré-moldados com consolos** — exatamente onde o DEB.Revit atua.

## Limitações conhecidas (experiência do DEB.Revit)
- API de **uma thread só**; tudo dentro de transações; regenerações lentas em modelos grandes.
- Com o Revit aberto a DLL do plugin fica travada (deploy exige fechar o Revit daquela versão).
- Detalhes de API: `GeometryInstance.Symbol` não existe no 2025 (usar `GetSymbolGeometryId`); diâmetros de barra
  por `SetBarTypeDiameters`; vigas unidas a pilares ganham **recuo de ½"** por padrão (zerar para consolos aninhados).
- Pranchas e textos dependem de famílias e templates do escritório (folha `Folhas Revit.rfa`).

## Importa / exporta
- RVT/RFA, IFC (exportador aberto), DWG/DXF, PDF, imagens, NWC, CAM (Precast), ligação com Robot.

## O que aproveitar para o nosso programa
1. **Usar a API de armadura moderna** no motor (D19): `RebarBendingDetail` para as formas cotadas da PP127,
   `MultiReferenceAnnotation` para as etiquetas, numeração e arredondamento nativos, emendas por regras.
2. **Revit Precast para alveolares e painéis**: avaliar se a **paginação de lajes alveolares** e os painéis (tilt-up,
   paredes) podem partir do Split e da armação do Precast, com o nosso motor completando o que falta (capeamento,
   detalhes NBR) — evita refazer o que a Autodesk já entrega.
3. **CAM Unitechnik/PXML** já resolvidos pelo Precast — referência de formato para a fase de fabricação.
4. Modelo analítico do Revit → Robot como caminho de análise (fase futura).

## A confirmar
- Testar o **Revit Precast** num modelo com laje alveolar (Split + armação + shop drawing) — já vem no 2025 e no 2026.
- `RebarBendingDetail` existe também na API do 2025 ✓ (compatível com os dois Revit do DEB.Revit).
