# IDEA StatiCa

| Fabricante | Licença | Plataforma | Instalado aqui |
|---|---|---|---|
| IDEA StatiCa (República Tcheca) | comercial | Windows (+ nuvem) | não |

## Módulos e funções
| Módulo | Faz |
|---|---|
| Connection | ligações metálicas por MEF/CBFEM (chapas, parafusos, soldas, chumbadores) |
| Member | estabilidade de barras metálicas com ligações |
| **Detail** | **regiões D de concreto por CSFM** (campo de tensões compatível): **consolos**, dentes Gerber, aberturas, apoios, ancoragens — verifica armadura e fissuras |
| RCS | seções de concreto armado/protendido (flexão composta, cisalhamento, torção, fissuração) |
| **Beam** | vigas protendidas e **pré-moldadas**, fases construtivas, perdas |
| Tendon / Prestressing | protensão |

## Análise
- MEF não linear local (ligações, regiões D); não é programa de pórtico global.

## Dimensionamento
- Eurocode, AISC/ACI, normas nacionais europeias, outras (NBR a confirmar — provável que não haja).

## Detalhamento
- Verifica a armadura que você desenha; não gera pranchas de fabricação.

## Importa / exporta
- **BIM links** com Revit, Tekla, SAP2000, ETABS, Robot, RFEM etc.; formato aberto **IOM (IDEA Open Model, XML)**.

## API / automação
- API .NET/Python, IOM para criar modelos programaticamente.

## Pré-moldados
- **Muito relevante**: consolos, dentes, apoios e vigas pré-moldadas são exatamente regiões D e fases.

## O que aproveitar
- CSFM / biela-tirante como **referência de verificação de consolos e dentes** (NBR 9062).
- IOM como exemplo de formato aberto de troca.

## A confirmar
- Normas disponíveis; custo; se existe versão de avaliação para testar os consolos do PP127.
