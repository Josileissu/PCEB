# SAP2000 e ETABS (CSI)

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| Computers and Structures, Inc. (Berkeley, EUA) | comercial; licença **Sentinel RMS** (chave local, rede ou "commuter") ✓ | Windows | ✓ **SAP2000 21.1.0** (`C:\Program Files\Computers and Structures\SAP2000 21`, 1,4 GB); ETABS não |

Levantamento de 05/11/2026 pelos arquivos instalados, manuais em PDF (`Manuals\`), `CSiRefer.pdf` (569 p.) e a
documentação da API (`CSI_OAPI_Documentation.chm`, ~1.900 páginas); o programa não foi aberto.

## Arquitetura e tecnologia ✓
- Interface **.NET/WPF** (`CSI.Core.UI`, `CSIRibbon`); 3D em **Direct3D 11 via SharpDX** (shaders próprios) com
  alternativa OpenGL (`CSIOpenGL`).
- Núcleo de cálculo em **Fortran Intel** com **OpenMP**; solver em processo separado (`CSI.SAPFire.Solver.exe` — motor
  "SAPFire", multi-thread, 64 bits).
- Banco de dados do modelo em tabelas (as mesmas do `.s2k`), exportáveis para Excel/Access/texto.
- Bibliotecas de perfis por país (`*.pro`: AISC, europeus, britânicos, chineses, indianos, japoneses, russos, alumínio),
  espectros e funções de tempo (`Spectra`, `Time History Functions`).

## Modelagem e análise ✓ (CSi Analysis Reference)
- **Elementos**: barra (frame) com seções gerais, rótulas plásticas, **cabo**, **casca** (fina/espessa, camadas),
  estado plano, axissimétrico, **sólido**, **link/apoio** (molas, amortecedores, isoladores, gaps, atrito, plasticidade),
  **tendão** (protensão), restrições e soldas, apoios distribuídos e não lineares.
- **Casos de carga** (pela API): estático linear e **multietapas**, estático não linear e **não linear em etapas
  (staged construction)**, modal (autovetores e Ritz), espectro de resposta, histórico no tempo linear/não linear
  (modal e direto), **flambagem**, **cargas móveis**, estado estacionário, PSD, hiperestático (protensão).
- Cargas automáticas (vento, sismo, ondas — normas estrangeiras; **sem NBR** no manual de cargas laterais).
- **Verificação**: 290 arquivos de exemplos de análise e notas técnicas em `Verification\` ✓ — base de casos-teste.

## Dimensionamento ✓
- Concreto (ACI 318, Eurocode 2, AS, BS, CSA, IS 456, TS 500, SP 63…), aço (AISC 360, Eurocode 3, CSA, IS 800,
  API, NTC…), alumínio, formados a frio. **Nenhuma norma brasileira** (NBR) na versão 21 — nem nos manuais nem na API.
- Não detalha armaduras: dá áreas de aço necessárias.

## API / automação ✓ — OAPI
`CSiAPIv1.dll` / `SAP2000v1.dll` (COM e .NET; Python por COM ou pythonnet; VBA). ~1.865 funções em:
**General** (30), **File** (8), **Edit** (35), **Select** (22), **Definitions** (846 — materiais, seções, casos,
combinações, padrões de carga, grupos, sistemas de coordenadas, massa, cortes de seção, pontes), **Object Model** (430 —
ponto, barra, casca, cabo, link, sólido, tendão; cargas, rótulas, modificadores), **Analysis Model** (108), **Analyze**
(12 — rodar, opções do solver, GDL ativos), **Analysis Results** (70 — deslocamentos, reações, esforços em barras,
cascas e sólidos, modos, fatores de flambagem, cortes de seção), **Design** (285 — aço, concreto, alumínio, formados
a frio), plugins dentro do SAP2000. Também roda o SAP2000 escondido (`ApplicationStart`, `Visible`).

## Importa / exporta ✓
- `.sdb` (binário) e **`.s2k`/`.$2k` (texto com as tabelas do banco)** — conversores `SapTranV8…V12`, `SAPTRANS`.
- DXF, **IFC** (`IfcImportExport`), **CIS/2** (aço), **CSiXRevit** (ida e volta com o Revit), SACS e StruCAD (offshore),
  **Perform-3D** (`Sap2Perform`), Excel/Access, relatórios configuráveis em XML.

## Pré-moldados
- Nada específico; útil para **análise em etapas** (articulado → engastado, montagem), ligações semirrígidas por links
  e protensão com tendões.

## O que aproveitar para o nosso programa
1. **Validação automática do nosso cálculo**: gerar o mesmo modelo pela OAPI (ou `.s2k`), rodar e comparar esforços e
   deslocamentos nos casos-teste — o SAP2000 vira o "gabarito" (não tem NBR, mas a análise é independente de norma).
2. **Exemplos de verificação** (`Verification\`, 290 arquivos) como fonte de casos-teste com resposta conhecida.
3. **Organização da API** (definições → objetos → análise → resultados → dimensionamento) como modelo para a API do
   núcleo do PCEB (que a IA e os plugins vão usar).
4. Lista de **casos de carga** (em etapas, flambagem, móveis, espectro) como checklist da fase de análise.

## A confirmar
- Licença ativa no escritório (Sentinel).
- Se a versão do escritório tem o CSiXRevit instalado e funcionando com o Revit 2025/2026.
