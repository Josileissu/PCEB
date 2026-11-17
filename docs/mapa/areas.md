# Áreas — caminho de hoje, teste e quem está mexendo

Filtro: `dotnet test tests\DEB.Core.Tests -c Release --filter "FullyQualifiedName~<filtro>"`. "Quem está mexendo" vem do
quadro (`docs/00_escopo/coordenacao.md`); confira lá antes de começar.

| Área | Caminho hoje | Teste (`--filter`) | Quem está mexendo |
|---|---|---|---|
| Barras, comprimento real, dobras, normas | `src/DEB.Core/Calculo`, `Normas`, `Descricao`, `Estribos` | `ComprimentoReal`, `Barra`, `Estribo`, `Normas` | — |
| Relação do aço, tabelas, numeração | `src/DEB.Core/RelacaoAco`, `Tabelas`, `Numeracao` | `Tabela`, `OrdenacaoMarcas` | — |
| Pilar pré-moldado (template) | `src/DEB.Core/Elementos/Pilar/PreMoldado` | `Pilar` | P1-1 (Revit) |
| Pilar pré-moldado do Revit (armadura 3D) | `src/DEB.Core/Elementos/Pilar/PreMoldado/Modelo3D`; `src/DEB.Revit/src/Precast` | `ParidadePilar` | O-2 (etapa 3) |
| Armadura por linha (longitudinais do pilar no CAD e no Revit; D32) | `src/DEB.Core/Elementos/Pilar/PreMoldado/ArmaduraPorLinha.cs`; no Revit `ColumnRebarSpec.ArmaduraPorLinha`, `ColumnRebarLayout.LongitudinalBars` e a tabela de `src/DEB.Revit/src/Precast/UI/PrecastColumnsWindow.cs` | `ArmaduraPorLinha`, `ParidadePilar` | O-2 (D32) |
| Consolo (catálogo, dados, tirante e costura, desenho e avisos) | `src/DEB.Core/Elementos/Consolo` (`DesenhoConsolo.cs` recebe um `ContextoConsolo`); a lista de consolos do pilar e onde eles aparecem nas vistas e cortes: `src/DEB.Core/Elementos/Pilar/PreMoldado/ConsolosPilar.cs` | `PilarConsolos`, `RegressaoVisual` | — |
| Viga pré-moldada (template) | `src/DEB.Core/Elementos/Viga/PreMoldada` | `Viga` | P2-8 |
| Blocos sobre estacas | `src/DEB.Core/Elementos/Fundacao/Bloco`, `src/DEB.Core/Motor/Fundacoes` | `BlocoEstacas`, `Motor` | P2-9 / P2-10 |
| Reservatório e base | `src/DEB.Core/Elementos/Reservatorio`, `src/DEB.Core/Elementos/Fundacao/BaseReservatorio` | `Reservatorio` | pausado (P2-7) |
| Motor de detalhamento | `src/DEB.Core/Motor` | `Motor` | P2-10 |
| Comum aos templates (kit de desenho, detalhes típicos, modelos salvos) | `src/DEB.Core/Documentacao`; contratos em `src/DEB.Core/Motor/Templates.cs` | `PreviaTemplate`, `ModelosTemplate`, `RegressaoVisual` | — |
| Regressão visual dos templates | `tests/DEB.Core.Tests/Referencias/*.txt` | `RegressaoVisual` | — |
| Seção de elemento linear (famílias, partes nomeadas, reconhecimento, faces 1…4 ↔ partes; D28) | `src/DEB.Core/Geometria/Secao`; `docs/04_arquitetura/secoes.md`; barras pela seção em `src/DEB.Core/Motor/SecaoBarras.cs` e arranjo por família (D31) em `src/DEB.Core/Motor/ArranjoFamilias.cs` | `Geometria` | O-2 (etapa 5) |
| Peças Revit → CAD | `src/DEB.Core/Intercambio`, `Pecas`; `src/DEB.Revit/src/Precast` | `Intercambio`, `ConversorLevantamento`, `SubdivisaoPecas`, `Reimportacao` | — |
| Laje alveolar | `src/DEB.Core/Lajes`; `src/DEB.Revit/src/LajeAlveolar` | `LajeAlveolar` | P1-8 |
| Formas (Revit) | `src/DEB.Core/Formas`; `src/DEB.Revit/src/Formas` | `Formas` | P1-9 |
| Importação QiCad, reconhecimento | `src/DEB.Core/Importacao`, `Reconhecimento` | `ImportacaoQiCad`, `Reconhecimento` | — |
| PDFs em lote no CAD (ARMPDFLOTE) | `src/DEB.Core/Documentacao/LotePdf.cs`, `EstiloPlotagemPadrao.cs` (PCEB.ctb), `FolhasModeloPdf.cs`, `ErrosLotePdf.cs`, `ArquivoPia.cs` + `TamanhosPc3.cs` (.pmp), `FormulariosPapel.cs`; `src/DEB.Cad/src/DEB.Cad/Pdf`, `Paleta/FormPdfLote.cs` | `LotePdf`, `LotePdfCtbErros`, `TamanhosPc3`, `FormulariosPapel`; `ARMTESTEPDFLOTE` no CAD | P2-11 |
| Plugin CAD (comandos, paleta, sincronizador) | `src/DEB.Cad/src` | autoteste no CAD (`comandos.md`) | — |
| Plugin Revit (comandos, prancha) | `src/DEB.Revit/src` | AutoRun no Revit (`comandos.md`) | — |
