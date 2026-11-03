# SAP2000 e ETABS (CSI)

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| Computers and Structures, Inc. (EUA) | comercial | Windows | ✓ SAP2000 21.1.0; ETABS não |

## Modelagem
- SAP2000: elementos gerais — barras, cascas, sólidos, links/molas, cabos, tendões; grids e templates.
- ETABS: focado em edifícios (pavimentos, diafragmas, paredes/núcleos).

## Análise
- Linear, não linear (geométrica e material, rótulas plásticas), **construção em etapas** (staged construction),
  modal, espectro de resposta, time-history, flambagem, cargas móveis, P-Δ, efeitos de longo prazo (fluência/retração).

## Dimensionamento
- Aço, concreto, alumínio, perfis formados a frio por ACI, AISC, Eurocode e outras.
  NBR: há opções brasileiras em algumas versões (a confirmar na 21).

## Detalhamento
- Não detalha armaduras para fabricação (apenas áreas de aço necessárias).

## Fabricação / CAM
- Não.

## Documentação
- Tabelas de resultados (exportáveis), relatórios, diagramas.

## Importa / exporta
- `.sdb` (binário), **`.s2k` / `.$2k` (texto, tabelas do banco de dados)** — o formato citado nos requisitos;
  ETABS `.edb` / `.e2k` (texto).
- Excel/Access (tabelas interativas), DXF, IFC, CIS/2, **CSiXRevit** (ponte com Revit).

## API / automação
- **OAPI** ✓: `CSiAPIv1.dll` e `CSI_OAPI_Documentation.chm` na pasta de instalação. Uso por .NET, Python (COM), VBA.
  Cria modelo, roda análise, lê resultados.

## Pré-moldados
- Útil para análise por **etapas** (montagem, ligações articuladas → engastadas), não para detalhamento.

## O que aproveitar
- `$2k / s2k`: formato texto simples para **exportar nosso modelo analítico** e conferir no SAP.
- OAPI para validar nosso motor de cálculo automaticamente (casos-teste).

## A confirmar
- Normas brasileiras disponíveis na versão 21; licença ativa.
