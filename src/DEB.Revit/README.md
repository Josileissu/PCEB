# Detalhamento Estrutural para Revit 2025 / 2026

Plugin em C# (.NET 8). Você seleciona uma viga ou um pilar, clica em **Detalhar peça**, e o plugin gera:

1. Um **assembly** da peça, alinhado ao eixo dela. Vigas inclinadas e pilares girados também funcionam.
2. As **vistas**:
   - Viga: elevação, vista superior, corte junto ao apoio (a uma distância = altura da viga) e corte no meio do vão.
   - Pilar: elevação frontal, elevação lateral e corte a meia altura.
   - Vista 3D e lista de peças.
3. As **cotas gerais**: comprimento e altura nas elevações, e largura × altura nos cortes.
4. A **folha**, com as vistas distribuídas sem sobreposição. O espaço do carimbo fica livre, a lista de peças vai acima do carimbo, e as escalas são reduzidas automaticamente se as vistas não couberem.

Funciona com uma peça ou com várias de uma vez (selecione todas antes de clicar). Se a peça já tiver assembly, o plugin pergunta se deve recriar o detalhamento ou pular a peça.

O nome do assembly, das vistas e da folha sai da **Marca** da peça (parâmetro *Mark*). Por isso vale preencher a marca antes de rodar, por exemplo "V12" ou "P3".

---

## Compilar e instalar

**Pré-requisitos:** Visual Studio 2022 (17.8 ou mais recente) com a carga de trabalho ".NET desktop" (já inclui o SDK do .NET 8), e o Revit 2025 e/ou 2026 instalados. Não é preciso nenhum pacote NuGet: as DLLs da API são lidas da pasta do Revit.

### Pelo Visual Studio
1. Abra o arquivo `StructuralDetailer.csproj`.
2. Compile (Ctrl+Shift+B). Por padrão, a compilação é para o Revit **2025**.
3. Ao final da compilação, o plugin é copiado automaticamente para `%AppData%\Autodesk\Revit\Addins\2025\`.
4. Abra o Revit. A aba **Detalhamento** aparece no Ribbon. Na primeira vez, o Revit pergunta se deve carregar o add-in: escolha "Sempre carregar".

### Pela linha de comando (as duas versões)
```
dotnet build -c Release -p:RevitVersion=2025
dotnet build -c Release -p:RevitVersion=2026
```
- Se o Revit estiver em outra pasta, acrescente `-p:RevitInstallDir="D:\Autodesk\Revit 2025\"`.
- Para compilar sem copiar para a pasta de Addins, acrescente `-p:DeployToRevit=false`.

> Feche o Revit antes de recompilar. Com o Revit aberto, a DLL fica bloqueada e a cópia falha.

---

## Configuração

O botão **Configurar** abre `%AppData%\StructuralDetailer\DetalhamentoConfig.json`. O arquivo `DetalhamentoConfig.exemplo.json` deste projeto mostra todos os campos. Os principais:

| Campo | Para que serve |
|---|---|
| `BeamElevationScale`, `ColumnElevationScale`, `SectionScale`, `View3DScale` | Escala inicial de cada tipo de vista (25 = 1:25). |
| `ScaleLadder` | Sequência de escalas usada quando as vistas não cabem na folha. |
| `ElevationViewTemplate`, `SectionViewTemplate`, `View3DViewTemplate` | Nome exato de um template de vista do projeto. **É aqui que entra o padrão gráfico do escritório**: espessuras de linha, hachuras, visibilidade, tipo de cota. |
| `TitleBlockName` | Trecho do nome do carimbo (ex.: "A1"). Vazio = primeiro carimbo do projeto. |
| `TitleBlockStampWidthMm/HeightMm` | Área do carimbo (canto inferior direito) que fica livre de vistas. |
| `SheetMargin*Mm` | Margens da folha. O padrão é 25 mm à esquerda e 10 mm nas outras (NBR 10068). |
| `BeamSupportSectionDistanceMm` | Distância do corte de apoio à extremidade da viga. 0 = usa a altura da viga. |

Se o template de vista controlar a escala, o template prevalece e o ajuste automático de escala não atua.

---

## Padrões de apresentação (pranchas)

A aparência das pranchas fica em arquivos JSON, um por tipo de elemento, em
`%AppData%\StructuralDetailer\Padroes\` (cópia versionada em `Padroes/` neste projeto).
Na primeira execução o plugin grava o padrão de fábrica; depois é só editar o arquivo.
Hoje o **pilar pré-moldado** (`pilarpremoldado.json`, baseado na prancha PP127) é usado pelo comando
"Pilares pré-moldados"; viga, laje e fundação já têm o mesmo formato para as próximas etapas.

| Seção do JSON | O que define |
|---|---|
| `Folha` | Carimbo (trecho do nome), número e nome da folha, parâmetros do carimbo a preencher, margens, escalas alternativas |
| `Unidades` | Unidade e precisão das cotas (`cm`/`mm`), nome do tipo de cota criado, afastamento |
| `Textos` | Fonte, altura dos textos, título grande da peça |
| `Armadura` | Partição da numeração (N1, N2…), formato dos rótulos (`{Qtd} N{N} ø{Diam} C={Comp}`, `{Qtd} N{N} c/{Espacamento}`), dobramentos |
| `Regioes` | Retângulos da área útil (frações 0–1, origem no canto inferior esquerdo) onde cada item é arranjado |
| `Vistas` | Lista ordenada: `Face` (1 = +X, 2 = −Y, 3 = −X, 4 = +Y), `Armadura`, `SecaoArmadura`, `Corte`, `Vista3D` — título, escala, região, cotas, armadura visível, template |
| `Tabelas` | `RelacaoDoAco` e `ResumoDoAco` (com peso), título e região |
| `Quadros` | Textos livres (informações de produção, notas) com campos `{Marca} {Quantidade} {Volume} {Peso} {fck}`… |
| `Legendas` | Nomes de legendas do projeto (detalhes padrão: dobras, emendas, aterramento) colocadas em toda prancha |
| `Producao` | Valores usados nos textos (`fck`, `fcjSaque`, `fcjMontagem`, densidade do concreto) |

Para criar uma variação (outro cliente, outro formato), copie o arquivo, mude `Nome` e ajuste —
o plugin usa o primeiro arquivo da pasta cujo `Elemento` corresponde.

---

## Estrutura do código

```
src/
  App.cs                     Ribbon (aba "Detalhamento")
  AutoRun.cs                 Execução sem interação para testes (SD_AUTORUN)
  Commands/
    DetailPieceCommand.cs    Seleção, TransactionGroup, relatório final
    PrecastColumnsCommand.cs Pilares pré-moldados: levantamento → janela → geração
    OpenConfigCommand.cs
  Precast/                   Pré-moldados: levantamento, grupos, armadura, pipeline, janela
  Presentation/              Padrões de apresentação: modelo JSON, vistas, tabelas de aço, prancha
  Core/
    PieceAnalyzer.cs         Tipo da peça, sistema local, dimensões, marca
    PieceDetailer.cs         Orquestra as etapas (uma transação por etapa)
    ViewFactory.cs           Vistas de assembly, cortes e posicionamento dos cortes
    DimensionService.cs      Cotas gerais a partir das faces extremas
    SheetBuilder.cs          Folha, carimbo, arranjo das vistas, ajuste de escala
    GeometryHelper.cs        Sólidos, referências de faces, vértices
    DetailingConfig.cs       Configuração em JSON
    DetailingReport.cs       Relatório exibido ao final
    Helpers.cs               Unidades, nomes únicos, supressão de avisos
  UI/                        Ícones e filtros de seleção
```

---

## Limitações desta fase (roteiro)

- **Fase 1 (atual):** peça retangular ou prismática, com assembly, vistas, cortes, cotas gerais e folha.
- **Fase 2:** cotas de elementos secundários (consolos, dentes Gerber, recortes, furos, insertos) e cotas encadeadas.
- **Fase 3:** geração da armadura (`Rebar`) a partir de parâmetros: estribos, longitudinais, armadura de consolo e de içamento.
- **Fase 4:** tags de armadura, tabela de aço (posição, Ø, quantidade, comprimento, peso) e vistas de dobra.
- **Fase 5:** janela de opções antes de gerar (escolha de vistas e cortes, folha de destino).

Ainda não suportados: vigas curvas e peças de outras categorias (por exemplo, Generic Model).
