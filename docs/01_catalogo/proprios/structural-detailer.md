# StructuralDetailer (plugin nosso para Revit)

| Origem | Licença | Plataforma | Situação |
|---|---|---|---|
| Escritório | própria | Revit 2025 e 2026, C# / .NET 8 | em desenvolvimento; código em `src/DEB.Revit` ✓ |

Detalhes técnicos: `desenvolvedor/CLAUDE.md`.

## Modelagem
- Usa o modelo do Revit. **Levantamento** dos pilares pré-moldados (sistema local, consolos como família aninhada
  compartilhada na viga), **grupos de peças iguais** (inclusive giradas 90/180/270°; espelhadas não) ✓.

## Análise
- Nenhuma.

## Dimensionamento
- Nenhum. Só **verificações construtivas** (NBR 6118 / 9062): Ø mínimo, espaçamentos, costura do consolo ≥ 0,4 tirante ✓.

## Detalhamento ✓
- Armadura padrão por grupo escolhida em janela WPF: longitudinal, estribos por zonas (topo/base/consolo),
  consolo (tirante + costura), içamento/insertos; modelos de armadura salvos.
- Geometria das barras em coordenadas locais (`ColumnRebarLayout`), usada pela prévia e pelo Revit.
- Rebar no Revit, marcas, partição N1…, parâmetros `SD_Aço` / `SD_Peso`.

## Documentação ✓
- Assembly por peça (pilar + consolos + barras), vistas, cotas, **prancha pelo padrão de apresentação** (PP127, A1, bilíngue),
  tabelas de armadura; peças iguais reaproveitam a folha.
- Padrões de apresentação em JSON (pilar, viga, laje, fundação previstos).

## Importa / exporta
- Nativos do Revit. Logs/diagnóstico em JSON; PNG da prancha nos testes.

## API / automação
- Comandos Revit + **AutoRun** (testes sem o usuário, abrindo o Revit por variáveis de ambiente) ✓.

## Pré-moldados
- Foco atual: pilares. Pendentes: marcadores de nível, etiquetas N, quadros de consolo, cotas de dobra em cm,
  carimbo real do escritório; depois vigas.

## O que aproveitar no projeto unificado
- Componente **C3 (plugin Revit)** do escopo; levantamento 3D e geração de pranchas no Revit.
- Pode passar a usar o `Armadura.Core` para regras de dobra, comprimento real e numeração (hoje cada plugin tem as suas).
