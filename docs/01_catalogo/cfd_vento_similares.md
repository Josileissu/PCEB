# Vento em estruturas — programas e similares

Do mais simples ao mais pesado. Fichas completas: `opensource/openfoam.md`, `comerciais/aerosim.md`.

| Programa | Tipo | Licença | Método | Para quê | Observação |
|---|---|---|---|---|---|
| **Visual Ventos** | norma | gratuito | NBR 6123 (estático) | forças de vento em galpões/edificações simples | brasileiro (confirmar origem/versão) |
| Módulos de vento TQS / Eberick / CYPE | norma | comercial | NBR 6123 | vento no modelo do edifício | já instalados (TQS, Eberick) |
| **OpenFOAM** | CFD | GPL-3 | volumes finitos (RANS/LES) | Cp, forças, vizinhança | aberto, pesado, Linux |
| **Code_Saturne** | CFD | GPL | volumes finitos | idem | EDF (França) |
| **SU2** | CFD | LGPL | volumes finitos | aerodinâmica | foco aeronáutico |
| **OpenLB / Palabos / waLBerla** | CFD | GPL/AGPL | **Lattice Boltzmann** | base aberta do mesmo método do AeroSim | uso acadêmico |
| **AeroSim** | CFD | comercial (nuvem) | Lattice Boltzmann em GPU | túnel de vento virtual para projeto | **brasileiro** |
| **SimScale** | CFD | comercial (nuvem, navegador) | OpenFOAM + LBM | vento em edifícios, conforto | referência de "CFD pela web" |
| **Orbital Stack** | CFD + IA | comercial (nuvem) | CFD/IA | vento e conforto em fase de concepção | |
| Ansys Fluent/CFX, Autodesk CFD | CFD | comercial | volumes/elementos finitos | geral | caros, uso geral |
| **ParaView** | pós | BSD | — | visualizar resultados de CFD/MEF | usa VTK (como o PrePoMax) |

## Leitura rápida
- O dia a dia é **NBR 6123**: o PCEB deve ter o cálculo normativo de vento (estático, depois dinâmico) embutido.
- CFD é exceção (formas atípicas, edifícios altos, vizinhança). Caminho mais realista: **integrar** com serviço
  (AeroSim/SimScale) ou rodar OpenFOAM em servidor na fase nuvem — não construir um CFD próprio.
- Túnel de vento físico (ensaios em laboratório) continua sendo a referência para validar CFD.
