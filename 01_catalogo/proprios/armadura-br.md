# Armadura BR (plugin nosso para CAD)

| Origem | Licença | Plataforma | Situação |
|---|---|---|---|
| Escritório (repositório privado `github.com/Josileissu/armadura-gstarcad`) | própria | **GstarCAD 2021** (principal); **AutoCAD 2021–2027** (port testado); ZWCAD 2024–2026 (SDK baixado, port não iniciado) | versão **0.1.23** (03/11/2026); código em `../armadura` ✓ |

Detalhes técnicos: `armadura/CLAUDE.md`, `docs/ARQUITETURA.md`, `docs/PROGRESSO.md`, `docs/TEMPLATES.md`.
Princípio declarado: **não dimensiona** — desenha, numera e quantifica; decisões de cálculo são do engenheiro.

## Modelagem
- Não modela estrutura; trabalha no **desenho 2D**. A "entidade" armadura é polilinha + textos + tabela nativos,
  com dados em XData (`ARMADURA_BR`) e comportamento por **Overrules** — o DWG abre editável sem o plugin ✓.

## Análise / Dimensionamento
- Nenhum (decisão do usuário). Regras normativas só de **detalhamento**: dobramentos e pinos (NBR 6118),
  traspasse (norma, nunca < 45φ, com nota) ✓ (`Core/Normas`).

## Detalhamento — ponto forte ✓
- **Barras** (ARMBARRA), edição por segmento, divisão, offset, rebatimento, cotas automáticas dos trechos.
- **Estribos** (ARMESTRIBO, repetição; poligonal fechada vira estribo com ganchos no canto superior esquerdo).
- **Faixas de distribuição** ortogonais/alinhadas, com escala própria e repetições ("2x11 N1 …").
- **Telas soldadas** (T1, T2…), **treliças**, **cordoalhas CP-190**.
- Chamadas, indicações, corte e dobra (coluna "DETALHE DE DOBRA"), cotas de corte.
- **Comprimento real** com desconto de dobras (planilha do usuário), ganchos com ângulo nominal.
- **Numeração** automática (global / individual / sequencial).
- **Detalhamentos** = blocos do CAD (criar/gerenciar), RA parcial por detalhamento.

## Templates de elementos (gera o detalhamento a partir de parâmetros) ✓
- **Base de reservatório** circular e retangular — feito.
- **Pilar pré-moldado PP-1** — em construção, etapas 1 a 5 feitas: forma, armaduras, consolos (**catálogo de
  consolos** ARMCONSOLOS), listas de longitudinais/estribos/consolos/níveis, **insertos por catálogo** (ARMINSERTOS),
  4 vistas, idioma PT/EN/PT-EN, padrão de apresentação do P56.
- Previstos: blocos de 1/2 estacas (com/sem cálice), viga pré-moldada (protendida ou não), pilar e viga in loco, lajes.
- Modelos dos templates editáveis em CSV (`%AppData%\ArmaduraBR\Templates\<id>.csv`); exportar/importar template.

## Fabricação / CAM
- Ainda não. Tem os dados de corte e dobra (base para BVBS no futuro).

## Documentação ✓
- **Relação do aço** (RA) inserir/atualizar/associar/bloquear, ordenada por N, resumo por peso (kg).
- Layers padrão por `layers.csv`; textos padrão; layouts/viewports com curvaturas.

## Importa / exporta ✓
- **Importa `.cad` do QiArmaduras** (AltoQi) — ARMIMPORTARCAD (`docs/FORMATO_QICAD.md`).
- **Reconhece** armaduras desenhadas em texto por outros programas (ARMRECONHECER).
- DWG/DXF nativos; leitura de DXF de referência em banco lateral (ARMTESTEDXF).
- Pendentes: RA para DOCX/XLSX.

## API / automação ✓
- C#: **`Armadura.Core` (netstandard2.0, sem CAD)** — modelo, normas, comprimentos, descrição, numeração, RA,
  importação, reconhecimento, templates; testes xUnit.
- `Armadura.Cad` (fonte compartilhada) compilada para GstarCAD (net48), AutoCAD (net48/net8/net10) e ZWCAD (futuro).
- Autoteste dentro do CAD (ARMTESTE*), instalador Inno Setup, versão demo de 1 hora.

## Pré-moldados
- Template de **pilar pré-moldado** no padrão do escritório (P56/P61), com consolos, insertos, içamento, emendas.
- Requisitos levantados em `docs/PILAR_PREMOLDADO_REQUISITOS.md` (categorias PP-1…PP-5).
- Normas do usuário indexadas em `info_auxiliar/indice_normas` (busca por `tools/normas/buscar.py`).

## O que aproveitar no projeto unificado
- **`Armadura.Core` já é um núcleo independente de CAD**: comprimento real, dobramentos, traspasse, numeração,
  descrição de barras, relação do aço, catálogos (bitolas, consolos, insertos). Pode servir também ao Revit e à web.
- É a peça que hoje cumpre o componente **C4 (plugin CAD/DXF)** do escopo.
- O índice de normas é o embrião do `02_biblioteca`.

## Pontos de unificação a decidir
- **Pilar pré-moldado existe em dois lugares**: template CAD (padrão P56/P61) e StructuralDetailer no Revit (padrão PP127).
  Regras, catálogos de consolos e numeração deveriam ter uma única fonte.
- Linguagem/plataforma: Core em netstandard2.0 (C#) — compatível com o StructuralDetailer (.NET 8).
