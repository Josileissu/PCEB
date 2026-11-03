# Autodesk Revit

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| Autodesk | comercial | Windows | ✓ 2025 (25.0.2) e 2026 (26.0.4) |

## Modelagem
- BIM 3D paramétrico: famílias (carregáveis, aninhadas, compartilhadas), tipos, parâmetros, assemblies, Parts.
- **Modelo analítico** independente do físico (`AnalyticalMember`, `AnalyticalPanel`, cargas, condições de contorno).

## Análise
- Nenhuma nativa; envia para Robot (integração ✓) ou outros (via API / Speckle / CSiXRevit).

## Dimensionamento
- Não.

## Detalhamento
- **Rebar** 3D (barras, conjuntos, formas de dobra, Area/Path Reinforcement, telas), cobrimentos, numeração.
- Vistas de assembly, cortes, cotas, etiquetas — o que o **StructuralDetailer** já automatiza ✓.

## Fabricação / CAM
- Extensão "Structural Precast for Revit" (segmentação, armadura de painéis, CAM Unitechnik/PXML) — não instalada.
- Exportação de dobra de barras (BVBS) só com extensões/terceiros.

## Documentação
- Folhas, tabelas (schedules), legendas, padrões de vista (view templates) — base das nossas pranchas.

## Importa / exporta
- RVT/RFA, IFC ✓ (IFC Parameters instalado), DWG/DXF, PDF, imagens, NWC.

## API / automação
- API .NET completa ✓ (plugin StructuralDetailer em C#/.NET 8). Também pyRevit/Dynamo.

## Pré-moldados
- Fortes: assemblies por peça, famílias paramétricas, pranchas por peça (já testado no StructuralDetailer).
- Fracos: sem cálculo; regenerações lentas; limitações já registradas no `CLAUDE.md`.

## O que aproveitar
- Revit como **modelador 3D e gerador de pranchas**; o cálculo fica fora (pergunta em aberto nº 2 do escopo).

## A confirmar
- Licença/possibilidade da extensão Precast; se vale usar o modelo analítico ou gerar o nosso a partir do físico.
